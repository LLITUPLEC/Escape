"""Bounded ELF note parsing and APK-to-symbol identity regression checks."""
import importlib.util
import io
from pathlib import Path
import struct
import unittest

SPEC = importlib.util.spec_from_file_location("android_symbols", Path(__file__).with_name("package_android_symbols.py"))
SYMBOLS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SYMBOLS)


def elf(bits=64, order="<", build_id=b"\x12" * 20, extra_note=b""):
    ident = b"\x7fELF" + bytes([2 if bits == 64 else 1, 1 if order == "<" else 2, 1]) + b"\0" * 9
    header_format = order + ("16sHHIQQQIHHHHHH" if bits == 64 else "16sHHIIIIIHHHHHH")
    ph_format = order + ("IIQQQQQQ" if bits == 64 else "IIIIIIII")
    hs, ps = struct.calcsize(header_format), struct.calcsize(ph_format)
    note = struct.pack(order + "III", 4, len(build_id), 3) + b"GNU\0" + build_id + extra_note
    header = struct.pack(header_format, ident, 3, 183 if bits == 64 else 40, 1, 0, hs, 0, 0, hs, ps, 1, 0, 0, 0)
    ph = struct.pack(ph_format, 4, 0, hs + ps, 0, 0, len(note), len(note), 4) if bits == 64 else struct.pack(ph_format, 4, hs + ps, 0, 0, len(note), len(note), 0, 4)
    return header + ph + note


class SymbolIdentityTests(unittest.TestCase):
    def test_both_android_abis_and_both_endian_notes(self):
        for bits, abi in ((32, "armeabi-v7a"), (64, "arm64-v8a")):
            for order in ("<", ">"):
                with self.subTest(bits=bits, order=order):
                    metadata = SYMBOLS.elf_metadata(io.BytesIO(elf(bits, order)))
                    self.assertEqual(metadata["buildId"], "12" * 20)
                    self.assertEqual(metadata["elfBits"], bits)
                    self.assertTrue(SYMBOLS.same_build(metadata, metadata, abi))

    def test_a_reused_filename_cannot_substitute_another_compilation_or_abi(self):
        source = SYMBOLS.elf_metadata(io.BytesIO(elf()))
        other = SYMBOLS.elf_metadata(io.BytesIO(elf(build_id=b"\x34" * 20)))
        self.assertFalse(SYMBOLS.same_build(source, other, "arm64-v8a"))
        self.assertFalse(SYMBOLS.same_build(source, source, "armeabi-v7a"))

    def test_conflicting_gnu_build_ids_are_not_arbitrarily_selected(self):
        second = struct.pack("<III", 4, 20, 3) + b"GNU\0" + b"\x56" * 20
        with self.assertRaisesRegex(ValueError, "unambiguous"):
            SYMBOLS.elf_metadata(io.BytesIO(elf(extra_note=second)))

    def test_truncated_note_is_rejected(self):
        data = bytearray(elf())
        struct.pack_into("<I", data, 120 + 4, 1000)
        with self.assertRaisesRegex(ValueError, "Truncated ELF note"):
            SYMBOLS.elf_metadata(io.BytesIO(data))

    def test_out_of_file_note_and_non_elf_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "outside"):
            SYMBOLS.elf_metadata(io.BytesIO(elf()[:-1]))
        with self.assertRaisesRegex(ValueError, "Expected ELF"):
            SYMBOLS.elf_metadata(io.BytesIO(b"invalid-file-with-more-than-sixteen-bytes"))


if __name__ == "__main__":
    unittest.main()
