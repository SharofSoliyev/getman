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
| Cookies received from responses | memory only | discarded when the app closes |
| Crash reports | `%APPDATA%\GetMan\crash.log` | written locally, never sent |

**Secrets in the workspace file are stored in plain text.** Passwords, bearer tokens, API keys,
client secrets, AWS keys and your Postman API key all live in `workspace.json` as you typed them,
exactly as Postman's own export files do. The file is protected only by Windows file permissions on
your user profile. Two consequences worth knowing:

- If `%APPDATA%` is on a roaming profile, the file — and the secrets in it — travel with it.
- Anything that can read your user profile can read those secrets.

Prefer environment variables (`{{token}}`) over hard-coded values where you can, so a secret lives
in one place you can clear.

Encrypting the secret fields with DPAPI is an open item; if that matters for your use, say so in an
issue.

## Scope

In scope: anything that lets a crafted collection, environment, export file, cURL command or script
read or write outside the workspace, execute arbitrary code, or exfiltrate data.

The JavaScript sandbox (Jint) runs pre-request and test scripts with a timeout and without file
system or process access. A way out of that sandbox is in scope.

Out of scope: the plain-text storage described above, which is documented behaviour rather than a
vulnerability, and anything requiring an attacker to already have write access to your user profile.
