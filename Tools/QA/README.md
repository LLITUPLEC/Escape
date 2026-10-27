# Local Lua checks

Run from any working directory with Python 3.12 or newer. The runners resolve the
repository themselves and use the private, ignored `.cache/lua-test-runtime`.
They run the actual Nakama modules against simulated storage and authentication;
they do not connect to a server.

Prepare a fresh checkout once:

```powershell
python -m pip install --target .cache/lua-test-runtime -r Tools/QA/requirements.txt
```

Run syntax checks, a selected suite, or every local Lua suite:

```powershell
python Tools/QA/run_lua.py --parse
python Tools/QA/run_lua.py Server/nakama/tests/battle_rounds_test.lua
python Tools/QA/run_lua_suite.py
```

Full-suite logs and the JSON summary default to `.cache/qa/backend-local`.
Use `--output <directory>` to retain a particular run elsewhere. Most suites use
Lua 5.1; the two concurrency interleaving suites use their documented Lua 5.4
runtime because they intentionally yield through `pcall`.
