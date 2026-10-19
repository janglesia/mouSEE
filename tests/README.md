# tests/

`make test` runs all of them.

| | |
|---|---|
| `test_*.py` | Python tests, one per tracker file (`test_gaze_model.py` tests `tracker/gaze_model.py`). Run with the `.venv` Python: `tracker/.venv/Scripts/python -m unittest discover -s tests` |
| [app.Tests/](app.Tests/) | C# tests for the app: `dotnet test tests/app.Tests` |
