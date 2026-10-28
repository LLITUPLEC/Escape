"""Package exact Android ELF symbols after a build, without rebuilding or touching Unity sources.

Every included ELF must have the same GNU Build-ID, class and machine as its
published APK library. Full IL2CPP DWARF and public symbol tables are mandatory.
Unity engine symbols are included when a matching installed source is available.
Uses Python's standard library only; never downloads a dependency.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import re
import struct
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[2]
ABI_MACHINE = {"arm64-v8a": (64, 183), "armeabi-v7a": (32, 40)}


def sha256(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def elf_metadata(stream):
    """Read bounded ELF headers/notes/section names; symbol payload stays on disk."""
    stream.seek(0, 2)
    size = stream.tell()

    def read(offset, count):
        if offset < 0 or count < 0 or offset + count > size:
            raise ValueError("ELF structure extends outside the file")
        stream.seek(offset)
        result = stream.read(count)
        if len(result) != count:
            raise ValueError("Truncated ELF structure")
        return result

    ident = read(0, 16)
    if ident[:4] != b"\x7fELF" or ident[4] not in (1, 2) or ident[5] not in (1, 2):
        raise ValueError("Expected ELF32/ELF64 with known byte order")
    bits = 64 if ident[4] == 2 else 32
    order = "<" if ident[5] == 1 else ">"
    header_format = order + ("16sHHIQQQIHHHHHH" if bits == 64 else "16sHHIIIIIHHHHHH")
    header = struct.unpack(header_format, read(0, struct.calcsize(header_format)))
    phoff, shoff, phsize, phcount, shsize, shcount, shstrings = header[5], header[6], header[9], header[10], header[11], header[12], header[13]
    if phcount > 4096 or shcount > 65535:
        raise ValueError("Unbounded ELF header table")
    notes = []
    phformat = order + ("IIQQQQQQ" if bits == 64 else "IIIIIIII")
    for index in range(phcount):
        if phsize < struct.calcsize(phformat):
            raise ValueError("Invalid ELF program header size")
        section = struct.unpack(phformat, read(phoff + index * phsize, struct.calcsize(phformat)))
        if section[0] == 4:  # PT_NOTE
            notes.append((section[2], section[5]) if bits == 64 else (section[1], section[4]))
    sections = []
    shformat = order + ("IIQQQQIIQQ" if bits == 64 else "IIIIIIIIII")
    for index in range(shcount):
        if shsize < struct.calcsize(shformat):
            raise ValueError("Invalid ELF section header size")
        section = struct.unpack(shformat, read(shoff + index * shsize, struct.calcsize(shformat)))
        sections.append(section)
        if section[1] == 7:  # SHT_NOTE (also present in symbol-only ELFs)
            notes.append((section[4], section[5]))
    names = b""
    if shstrings < len(sections):
        section = sections[shstrings]
        if section[5] > 16 * 1024 * 1024:
            raise ValueError("ELF section-name table exceeds the parser bound")
        names = read(section[4], section[5])
    debug_sections = []
    symtab_entries = 0
    for section in sections:
        offset = section[0]
        name = names[offset:].split(b"\0", 1)[0].decode("ascii", errors="replace") if offset < len(names) else ""
        if name.startswith((".debug_", ".zdebug_")):
            debug_sections.append(name)
        if section[1] == 2 and section[9]:  # SHT_SYMTAB, not merely exported dynamic symbols
            symtab_entries += section[5] // section[9]
    build_ids = set()
    for offset, count in set(notes):
        if count > 16 * 1024 * 1024:
            raise ValueError("ELF note exceeds the parser bound")
        note = read(offset, count)
        cursor = 0
        while cursor + 12 <= len(note):
            name_size, description_size, kind = struct.unpack_from(order + "III", note, cursor)
            cursor += 12
            if cursor + name_size > len(note):
                raise ValueError("Truncated ELF note name")
            name = note[cursor:cursor + name_size].rstrip(b"\0")
            cursor += (name_size + 3) & ~3
            if cursor + description_size > len(note):
                raise ValueError("Truncated ELF note description")
            description = note[cursor:cursor + description_size]
            cursor += (description_size + 3) & ~3
            if name == b"GNU" and kind == 3 and description:
                build_ids.add(description.hex())
    if len(build_ids) != 1:
        raise ValueError("Expected one unambiguous GNU Build-ID")
    return {"buildId": next(iter(build_ids)), "elfBits": bits, "machine": header[2],
            "symbolTableEntries": symtab_entries, "debugSections": debug_sections}


def same_build(symbol, apk, abi):
    return ((symbol["elfBits"], symbol["machine"]) == ABI_MACHINE[abi]
            and all(symbol[key] == apk[key] for key in ("buildId", "elfBits", "machine")))


def label(path, engine_root):
    try:
        return path.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        if path.resolve().is_relative_to(engine_root.resolve()):
            return "unity-editor-symbols/" + path.resolve().relative_to(engine_root.resolve()).as_posix()
        return "external-symbol-source/" + path.name


def verify_archive(path, manifest):
    with zipfile.ZipFile(path) as archive:
        expected = {entry["archivePath"]: entry for entry in manifest["symbols"]}
        if set(archive.namelist()) != set(expected) | {"symbols-manifest.json"}:
            raise ValueError("Unexpected symbols archive contents")
        if json.loads(archive.read("symbols-manifest.json")) != manifest:
            raise ValueError("Symbols archive manifest mismatch")
        for name, entry in expected.items():
            digest = hashlib.sha256()
            size = 0
            with archive.open(name) as stream:
                for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
                    digest.update(block)
                    size += len(block)
            # Reading through EOF also verifies the ZIP CRC for every entry.
            if digest.hexdigest() != entry["sha256"] or size != entry["bytes"]:
                raise ValueError("Archived symbol hash/size mismatch: " + name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk-validation", type=Path, required=True, help="Existing validated and published APK identity JSON")
    parser.add_argument("--apk", type=Path, help="Defaults to Builds/<validated version>/<validated APK filename>")
    parser.add_argument("--il2cpp-build-root", type=Path, default=ROOT / "Library/Bee/artifacts/Android/il2cppOutput/build")
    parser.add_argument("--engine-symbols-root", type=Path, help="Unity AndroidPlayer/Variations/il2cpp directory; defaults to this project's installed Unity version")
    parser.add_argument("--output", type=Path, help="Defaults beside APK, with the same stem and .symbols.zip")
    parser.add_argument("--report", type=Path, help="Defaults beside the validated APK report as symbols-validation.json")
    parser.add_argument("--compression-level", type=int, choices=range(1, 10), default=1)
    args = parser.parse_args()
    validation = json.loads(args.apk_validation.read_text(encoding="utf-8-sig"))
    if not (validation.get("build_result") == "Succeeded" and validation.get("signature_verified") is True
            and validation.get("development_build") is False and validation.get("debuggable") is False):
        raise ValueError("A validated signed release APK is required")
    apk = (args.apk or ROOT / "Builds" / validation["version"] / validation["apk"]).resolve(strict=True)
    if apk.stat().st_size != validation["bytes"] or sha256(apk) != validation["sha256"]:
        raise ValueError("APK differs from the validated published identity")
    output = (args.output or apk.with_name(apk.stem + ".symbols.zip")).resolve()
    report = (args.report or args.apk_validation.resolve().with_name("symbols-validation.json")).resolve()
    if not output.is_relative_to(ROOT / "Builds") or output.is_symlink() or not report.is_relative_to(ROOT):
        raise ValueError("Archive must be under Builds and report under this project")
    engine_root = args.engine_symbols_root
    if engine_root is None:
        version = re.search(r"m_EditorVersion: (\S+)", (ROOT / "ProjectSettings/ProjectVersion.txt").read_text()).group(1)
        engine_root = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Unity/Hub/Editor" / version / "Editor/Data/PlaybackEngines/AndroidPlayer/Variations/il2cpp"
    apk_libraries = {}
    with zipfile.ZipFile(apk) as archive:
        for entry in archive.namelist():
            match = re.fullmatch(r"lib/(arm64-v8a|armeabi-v7a)/(lib(?:il2cpp|unity|main)\.so)", entry)
            if match:
                apk_libraries[(match[1], match[2])] = elf_metadata(io.BytesIO(archive.read(entry)))
    abis = sorted(abi for abi, library in apk_libraries if library == "libil2cpp.so")
    if set(abis) != set(validation["native_abis"]) or set(abis) != set(ABI_MACHINE):
        raise ValueError("Expected exactly the validated ARM64 and ARMv7 IL2CPP libraries")
    selected = []
    for abi in abis:
        apk_elf = apk_libraries[(abi, "libil2cpp.so")]
        built = args.il2cpp_build_root / abi / "libil2cpp.so"
        with built.open("rb") as stream:
            if not same_build(elf_metadata(stream), apk_elf, abi):
                raise ValueError("Compiled IL2CPP binary does not match APK Build-ID: " + abi)
        for suffix in ("sym", "dbg"):
            source = args.il2cpp_build_root / abi / ("libil2cpp." + suffix + ".so")
            with source.open("rb") as stream:
                metadata = elf_metadata(stream)
            if not same_build(metadata, apk_elf, abi) or metadata["symbolTableEntries"] < 1:
                raise ValueError("IL2CPP symbols do not match APK or contain no symbol table: " + abi + "/" + suffix)
            if suffix == "dbg" and not metadata["debugSections"]:
                raise ValueError("Full IL2CPP DWARF sections are missing: " + abi)
            selected.append((source, {"archivePath": abi + "/" + source.name, "library": "libil2cpp.so", "abi": abi,
                                     "kind": "full-debug" if suffix == "dbg" else "public-symbols", **metadata}))
        for library in ("libunity.so", "libmain.so"):
            if (abi, library) not in apk_libraries:
                continue
            for suffix in ("sym", "dbg"):
                filename = library[:-3] + "." + suffix + ".so"
                candidates = [ROOT / "Library/Bee/artifacts/Android/libunity" / abi / filename] if library == "libunity.so" else []
                if engine_root.is_dir():
                    candidates.extend(sorted(engine_root.glob("*/Symbols/" + abi + "/" + filename)))
                for source in candidates:
                    if not source.is_file():
                        continue
                    with source.open("rb") as stream:
                        metadata = elf_metadata(stream)
                    if same_build(metadata, apk_libraries[(abi, library)], abi) and metadata["symbolTableEntries"] > 0:
                        selected.append((source, {"archivePath": abi + "/" + filename, "library": library, "abi": abi,
                                                 "kind": "engine-debug" if suffix == "dbg" else "engine-public-symbols", **metadata}))
                        break
    manifest = {"schema": 1, "version": validation["version"], "versionCode": validation["version_code"],
                "apk": apk.name, "apkBytes": validation["bytes"], "apkSha256": validation["sha256"],
                "provenance": "ELF GNU Build-ID and ABI match the already published signed APK; no rebuild performed",
                "abis": abis, "symbols": []}
    if output.exists():
        expected_manifest = manifest
        with zipfile.ZipFile(output) as archive:
            manifest = json.loads(archive.read("symbols-manifest.json"))
        for key in ("schema", "version", "versionCode", "apk", "apkBytes", "apkSha256", "abis"):
            if manifest.get(key) != expected_manifest[key]:
                raise ValueError("Existing symbols manifest differs from the validated APK identity")
        existing = {entry["archivePath"]: entry for entry in manifest["symbols"]}
        if len(existing) != len(manifest["symbols"]) or set(existing) != {entry["archivePath"] for _, entry in selected}:
            raise ValueError("Existing archive does not contain the verified symbol selection")
        for source, entry in selected:
            saved = existing[entry["archivePath"]]
            if any(saved.get(key) != value for key, value in entry.items()) or saved["sha256"] != sha256(source) or saved["bytes"] != source.stat().st_size:
                raise ValueError("Existing archived symbols do not match these verified compiled inputs")
        verify_archive(output, manifest)
    else:
        output.parent.mkdir(parents=True, exist_ok=True)
        temporary = output.with_name(output.name + ".partial-" + uuid.uuid4().hex)
        try:
            with zipfile.ZipFile(temporary, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=args.compression_level, allowZip64=True) as archive:
                for source, entry in selected:
                    stat = source.stat()
                    digest = hashlib.sha256()
                    with source.open("rb") as stream, archive.open(entry["archivePath"], "w", force_zip64=True) as destination:
                        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
                            digest.update(block)
                            destination.write(block)
                    if (source.stat().st_size, source.stat().st_mtime_ns) != (stat.st_size, stat.st_mtime_ns):
                        raise ValueError("A compiled symbol source changed while packaging")
                    entry.update(bytes=stat.st_size, sha256=digest.hexdigest(), source=label(source, engine_root))
                    manifest["symbols"].append(entry)
                    print("Packed " + entry["archivePath"], flush=True)
                archive.writestr("symbols-manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
            verify_archive(temporary, manifest)
            if output.exists():
                raise FileExistsError("Symbols output appeared concurrently; refusing replacement")
            temporary.rename(output)
        finally:
            if temporary.exists():
                temporary.unlink()  # Only this invocation's unique partial file.
    if sha256(apk) != validation["sha256"]:
        raise ValueError("APK changed during symbols packaging")
    result = {"passed": True, "version": validation["version"], "versionCode": validation["version_code"],
              "apk": apk.name, "apkSha256": validation["sha256"], "archive": output.relative_to(ROOT).as_posix(),
              "archiveBytes": output.stat().st_size, "archiveSha256": sha256(output), "abis": abis,
              "allGnuBuildIdsMatchApk": True, "allSymbolSha256AndZipCrcVerified": True,
              "fullIl2cppDebugIncluded": True, "sourceInputsDeleted": False, "apkRebuilt": False,
              "checkedUtc": datetime.now(timezone.utc).isoformat(), "symbols": manifest["symbols"]}
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("passed", "archive", "archiveBytes", "archiveSha256", "allGnuBuildIdsMatchApk")}))


if __name__ == "__main__":
    main()
