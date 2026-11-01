## What

<!-- What changed. Include the task ID, for example T-001. -->

## Why

<!-- The task or story this serves. -->

## How tested

<!-- How the change was verified. -->

## Deviations

<!-- Differences from the build spec. Should be none. -->

## Follow-ups

<!-- Work this PR leaves for later. -->

## Definition of Done

- [ ] Code compiles with zero warnings (`TreatWarningsAsErrors`)
- [ ] `dotnet format --verify-no-changes` passes
- [ ] All existing tests pass (no skipped, no flaky)
- [ ] New behavior has new tests (referencing FM or US IDs)
- [ ] Architecture tests pass
- [ ] OpenAPI snapshot is unchanged or explicitly updated
- [ ] No secrets in the diff (`git diff --cached | grep -iE 'password|secret|key|token'` is clean)
- [ ] PR description has: what, why, how tested, deviations (should be none), follow-ups
- [ ] PR is under 400 changed lines (excluding migrations and generated files)
- [ ] CI is green
