# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Use GitHub's [private vulnerability reporting](https://github.com/SharofSoliyev/getman/security/advisories/new)
on this repository, or send a private message to the maintainer on GitHub. Include what you found,
how to reproduce it, and what an attacker could do with it. You will get an acknowledgement within
a few days, and credit in the fix unless you would rather stay anonymous.

## What GetMan stores, and where

GetMan is a desktop application with no server component. It never uploads your workspace anywhere.

| What | Where | Notes |
|---|---|---|
| Collections, environments, history, settings | `%APPDATA%\GetMan\workspace.json` | one file per Windows user, plus a rolling `workspace.backup.json` |
| Passwords, tokens and keys | the same file, DPAPI-encrypted | see below |
| Cookies received from responses | memory only | discarded when the app closes |
| Crash reports | `%APPDATA%\GetMan\crash.log` | written locally, never sent |

**Secrets in the workspace file are encrypted**, by default, with Windows DPAPI scoped to your user
account (Settings → General → Storage turns it off). That covers passwords, bearer tokens, API key
values, OAuth client secrets and tokens, AWS secret keys and session tokens, Hawk keys, the proxy
and client-certificate passwords, your Postman API key, and any variable you mark as secret.

What that does and does not buy you:

- Another process running as **a different user** on the machine cannot read them, and neither can
  anyone who copies `workspace.json` off the machine — including a roaming `%APPDATA%`, where the
  file travels but the secrets do not decrypt.
- Anything running **as you** still can, because DPAPI hands the key to your account on request.
  Encryption at rest is not a defence against code you have already run.
- Move the file to another machine or user and the secrets come back as `getman:enc:v1:…` rather
  than blank. That is deliberate: a blank field looks like the secret was never set, and a request
  would then authenticate as nobody instead of failing.

**Exports are never encrypted.** `Export as Postman v2.1` and environment exports are meant to open
in Postman and in other people's tools, so treat an export the way you would treat any file with a
token in it.

Prefer environment variables (`{{token}}`) over hard-coded values where you can, so a secret lives
in one place you can clear.

## Scope

In scope: anything that lets a crafted collection, environment, export file, cURL command or script
read or write outside the workspace, execute arbitrary code, or exfiltrate data.

The JavaScript sandbox (Jint) runs pre-request and test scripts with a timeout and without file
system or process access. A way out of that sandbox is in scope.

Out of scope: reading a secret from a process already running as the same Windows user, which DPAPI
does not and cannot prevent, and anything requiring an attacker to already have write access to your
user profile.
