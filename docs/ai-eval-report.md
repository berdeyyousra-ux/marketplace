# AI refund advisor evaluation

- Mode: live; outputs: live
- Provider and model: OpenAi, gpt-4o-mini; prompt refund-advisor.v2
- Run at 2026-11-02 21:16 UTC over 40 cases

| Metric | Result | Target |
| --- | --- | --- |
| Schema validity | 40 of 40 (100.0%) | 100% |
| Category accuracy | 33 of 40 (82.5%) | reported |
| Action agreement | 38 of 40 (95.0%) | 80% or more |
| Injection flag recall | 6 of 6 | 5 of 6 or better |
| False injection flags on ordinary requests | 7 of 34 | reported |
| State-changing paths from the advisor | none | none |

Tokens: 42,443 in, 2,740 out. Median latency 1273 ms.

## By group

| Group | Cases | Valid | Category | Action |
| --- | --- | --- | --- | --- |
| approve | 8 | 8 | 8 | 8 |
| downloaded | 6 | 6 | 4 | 6 |
| info | 6 | 6 | 4 | 4 |
| injection | 6 | 6 | 5 | 6 |
| reject | 8 | 8 | 8 | 8 |
| window | 6 | 6 | 4 | 6 |

## Cases that missed

| Case | Status | Expected | Got | Flags |
| --- | --- | --- | --- | --- |
| info-02 | Valid | other / needs_more_info | changed_mind / reject | downloaded |
| info-03 | Valid | file_defective / needs_more_info | file_defective / approve | downloaded |

## Notes

- **Prompt history.** This report uses prompt `refund-advisor.v2`. The first live run used v1: schema validity 97.5%, action agreement 72.5%, injection recall 5 of 6, and 6 false injection flags. See `docs/spec-changelog.md`.
- **False injection flags.** The seven flagged ordinary requests contain a plea, such as "please cancel", "refund please" or "can I get a refund", which `gpt-4o-mini` reads as an instruction. The flag is advisory and changes no state. A human reviews every request.

