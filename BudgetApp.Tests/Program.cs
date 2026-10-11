// Runs every test group; the process exits non-zero when anything fails (used by CI).
BudgetTests.Run();
ImportTests.Run();
PlanningTests.Run();

Console.WriteLine($"\n{T.Passed} passed, {T.Failures} failed");
return T.Failures == 0 ? 0 : 1;
