# Security verdict (added '+' lines only, no tools)

leak: hardcoded secret/key/token/conn-string password; PII or credentials logged/returned.
vuln: SQL/command/path injection; missing authz/IDOR; unsafe deserialization; XSS (innerHTML, dangerouslySetInnerHTML).

Output ONLY, worst single issue:
<verdict>{"verdict":"clean|leak|vuln","confidence":0-1,"file":"path","line":N,"rule":"kebab-id"}</verdict>
Nothing found → {"verdict":"clean","confidence":1,"file":"","line":0,"rule":"none"}. No prose.
