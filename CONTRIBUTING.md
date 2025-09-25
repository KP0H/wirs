# Contributing

This repo uses **GitFlow** for branch model and **Conventional Commits** for messages & PR titles.

---

## Branch model (GitFlow)

**Permanent branches**
- `master` — production-ready code.
- `develop` — integration branch for next release.

**Prefixes (configured via gitflow):**
- `feat/`     — feature branches (GitFlow: `feature`)
- `fix/`   — bugfix branches (GitFlow: `bugfix`)
- `release/`  — release preparation
- `hotfix/`   — hot fixes from `master`
- `support/`  — long-lived support branches (rarely used)
- `versiontag` — *empty* (we tag releases like `vX.Y.Z`)

**Naming convention**
```
feat/m3-hmac-validation-42
fix/108-retry-jitter
hotfix/1.0.1-nullref-on-startup
release/1.0.0
docs/readme-rfcs
chore/ci-enforce-issue-link
```

---

## Conventional Commits

**Types** we use in commit messages & PR titles:
- `feat` | `fix` | `docs` | `chore` | `refactor` | `perf` | `test` | `ci` | `build`
**Scope** examples: `api`, `worker`, `ui`, `infra`, `ci`, `docs`

Examples:
```
feat(api): add inbox endpoint
fix(worker): correct jitter backoff
docs(readme): add RFC/Milestones section
chore(ci): enforce linked issue via GitHub Action
```

PR titles must also follow Conventional Commits.

---

## Linking issues (required)

Every PR **must** reference an issue with closing keywords in title or body:
```
Closes #123
Fixes #123
Resolves #123
```

We enforce this with a PR workflow; missing link will **fail** the check.

---

## PR checklist

- [ ] Title follows Conventional Commits
- [ ] PR links issue with `Closes #<id>`
- [ ] Tests added/updated (where applicable)
- [ ] Docs updated (README / RFC / ADR) if needed
