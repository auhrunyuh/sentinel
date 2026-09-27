# Security verdict (added '+' lines only, no tools)

leak:
  - Hardcoded secret/key/token/connection-string/password in source or config
  - PII or credentials in ILogger.Log*/LogInformation/LogError/LogDebug calls or HTTP responses

vuln:
  - Missing [Authorize] (or equivalent policy/role) on new controller actions or minimal-API endpoints
  - Over-posting: model-bound class exposes properties not present in [BindProperty]/DTO (use [Bind] or a dedicated input model)
  - Raw SQL string concatenation or interpolation (use parameterised queries / EF Core)
  - Path traversal: user input flows into File.*/Directory.*/Path.Combine without canonicalisation + base-path check
  - Command injection; unsafe deserialisation; IDOR; XSS (innerHTML, dangerouslySetInnerHTML)

Output ONLY, worst single issue:
<verdict>{"verdict":"clean|leak|vuln","confidence":0-1,"file":"path","line":N,"rule":"kebab-id"}</verdict>
Nothing found → {"verdict":"clean","confidence":1,"file":"","line":0,"rule":"none"}. No prose.
