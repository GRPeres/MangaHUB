# ChatGPT plan for scheduled issue proposals

This connection is for a private self-hosted MangaHub installation. It uses
MangaHub's own OpenAI authorization, not a Codex login file. With the credential
path configured, manual and scheduled drafts both use the ChatGPT connection.

1. On the computer where you will sign in, use Python 3 and install the crypto
   dependency in a virtual environment: `python -m pip install "PyJWT[crypto]"`.
2. Choose a stable `urn:uuid:<UUID>` identifier for that installation. Run
   `python scripts/connect-mangahub-chatgpt.py /private/path/chatgpt.json --host-id urn:uuid:<UUID>`
   from a local checkout, with the output outside the repo in a directory only
   you can access. Sign in on OpenAI's page and grant plan usage.
3. Transfer the resulting file over SSH to a private NAS directory. Allow only
   the API container's user to read and write it. Mount the **directory** read-write
   in the API container so rotating credentials can be replaced atomically.
   Do not mount it into the worker or web container or include it in shared backups.
4. Set these environment variables on the API, then redeploy:

   ```yaml
   MangaHub__IssueProposals__ChatGptCredentialFile: /issue-proposal-auth/chatgpt.json
   MangaHub__IssueProposals__Model: "<model eligible for your ChatGPT plan>"
   ```

5. Run **Admin > Operations > Diagnostics > Test ChatGPT connection**. Then
   open **Admin > Issues > AI proposals > Schedule** to enable scheduling.
   Only the NAS should use the transferred
   session. Remove the local copy after the transfer.

To disconnect, disable the schedule, remove the credential file from the
mounted directory, and revoke MangaHub's access in ChatGPT settings. To
reconnect, authorize a new credential file and install it again. The status
indicator is green after a successful provider request. No API-key fallback
is used when the credential path is configured.

See [OpenAI's self-hosted setup](https://developers.openai.com/siwc/token-sharing-open-source/self-hosted-vms)
and [preview limits](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations).

## Optional hiatus research

Use **Web research for stalled releases** in **Admin > Issues > AI proposals > Schedule**.
It defaults to on, but runs only when automatic proposals are enabled. The selected OpenAI model
and account must support web search; unsupported requests appear as run errors.
Drafts show cited links and must be reviewed by an admin. Missing citations
produce an uncertain result. Research never changes publication status.
