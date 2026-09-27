# Performance verdict (added '+' lines only, no tools)

slow: EF N+1 (nav in loop, no Include); unbounded query (no Take/paging); ToList before Where; missing AsNoTracking on reads;
.Result/.Wait()/GetAwaiter().GetResult(); allocations/LINQ or string += in hot loops; TS await-in-loop, re-render props.

Output ONLY, worst single issue:
<verdict>{"verdict":"clean|slow","confidence":0-1,"file":"path","line":N,"rule":"kebab-id"}</verdict>
Nothing found → {"verdict":"clean","confidence":1,"file":"","line":0,"rule":"none"}. No prose.
