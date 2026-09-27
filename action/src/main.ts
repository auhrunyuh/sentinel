import * as core from "@actions/core";
import * as github from "@actions/github";
import * as exec from "@actions/exec";

async function execCapture(cmd: string, args: string[], env?: Record<string, string>): Promise<string> {
  let out = "";
  await exec.exec(cmd, args, {
    env,
    listeners: { stdout: (d) => (out += d.toString()) },
  });
  return out;
}

async function run(): Promise<void> {
  const bobApiKey = core.getInput("bob-api-key", { required: true });
  core.setSecret(bobApiKey);
  const githubToken = core.getInput("github-token") || process.env.GITHUB_TOKEN || "";
  const maxCost = core.getInput("max-cost") || "0.5";
  const mode = core.getInput("mode") || "sentinel-triage";

  const payload = github.context.payload as any;
  const workflowRun = payload.workflow_run;
  if (!workflowRun) {
    core.setFailed("Not a workflow_run event — no failed run to triage.");
    return;
  }
  const runId: number = workflowRun.id;
  // workflow_run doesn't carry a PR number directly for forked/failed runs; pull_requests[] is
  // populated for same-repo PRs, which covers the v0 case (see sentinel.md 4.7 / action/action.yml).
  const prNumber: number | undefined = workflowRun.pull_requests?.[0]?.number;

  const ghEnv = { ...process.env, GH_TOKEN: githubToken } as Record<string, string>;
  const log = await execCapture("gh", ["run", "view", String(runId), "--log-failed"], ghEnv);
  const tail = log.split("\n").slice(-400).join("\n");

  // ponytail: bobshell.sh installer path varies by version; add both known bin dirs rather
  // than parsing installer output. Upgrade to reading the installer's own PATH hint if it breaks.
  await exec.exec("sh", ["-c", "curl -fsSL https://bob.ibm.com/download/bobshell.sh | bash"]);
  core.addPath(`${process.env.HOME}/.bob/bin`);
  core.addPath(`${process.env.HOME}/.local/bin`);

  const prompt = [
    "Triage this CI failure. Read-only: do not edit files.",
    "Identify the root cause and cite the offending file/line if visible in the log.",
    "",
    "--- failed job log (last 400 lines) ---",
    tail,
  ].join("\n");

  let bobStdout = "";
  await exec.exec(
    "bob",
    [
      "run",
      "--mode", mode,
      "--format", "json",
      "--max-cost", maxCost,
      "--max-turns", "15",
      "--accept-license",
      "--trust",
    ],
    {
      input: Buffer.from(prompt, "utf8"),
      env: { ...process.env, BOB_API_KEY: bobApiKey },
      listeners: { stdout: (d) => (bobStdout += d.toString()) },
    }
  );

  const lastLine = bobStdout.trim().split("\n").filter(Boolean).pop();
  if (!lastLine) {
    core.setFailed("bob run produced no output.");
    return;
  }
  const result = JSON.parse(lastLine);
  const message: string = result.last_message ?? "(no message)";
  const cost = result.stats?.session_costs ?? "unknown";

  core.setOutput("root-cause", message);
  core.setOutput("cost", String(cost));

  if (!prNumber) {
    core.info("No associated PR — skipping comment.");
    return;
  }

  const octokit = github.getOctokit(githubToken);
  const body = [
    "### 🔍 Sentinel triage",
    "",
    message,
    "",
    `<sub>mode: \`${mode}\` · cost: ${cost} Bobcoins</sub>`,
  ].join("\n");
  await octokit.rest.issues.createComment({
    ...github.context.repo,
    issue_number: prNumber,
    body,
  });
}

run().catch((err) => core.setFailed(err instanceof Error ? err.message : String(err)));
