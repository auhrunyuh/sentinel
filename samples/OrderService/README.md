# OrderService (sample victim app)

Small ASP.NET Core minimal API (in-memory orders, pricing, paging, shipping, quantity parsing). Own solution: `dotnet test OrderService.slnx`. Clean code = correct, all tests green.

- `bugs/NN.patch` injects bug NN: 01 paging off-by-one, 02 null address NRE, 03 banker's rounding, 04 culture-dependent parse, 05 negative total from stacked discounts.
- `alerts/NN.json` is the synthetic prod alert the bug would raise (feed this to the agent).
- `hidden-tests/BugNNTests.cs` fails with the bug, passes clean. Not compiled; never show it to the agent.

Inject (patch paths are relative to this folder, so copy it into its own repo first):
`cp -r samples/OrderService /tmp/os && cd /tmp/os && git init -q && git add -A && git commit -qm clean && git apply bugs/01.patch`
Existing tests still pass with any bug injected. Undo with `git apply -R bugs/01.patch`.
