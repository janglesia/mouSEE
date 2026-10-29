# app.Tests/

The C# app's tests (xUnit). The folders are the same as in `app/`, and each file tests one class, named after it: `Calibration/GazeGrid/GazeFitTests.cs` tests `GazeFit`. To find the tests for a file, look in the same folder here.

| | |
|---|---|
| `Fakes/` | fake frames and recordings that a few test files share |
| `StatsTests.cs` | `app/Stats.cs` |
| everything else | the folder of the same name in `app/` |

Run with `make test`, or `dotnet test tests/app.Tests`.
