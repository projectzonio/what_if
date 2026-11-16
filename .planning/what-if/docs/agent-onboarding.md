# Agent instructions

1. Read `README.md`, `docs/source-of-truth.md`, `docs/architecture.md`, `docs/gates.md`, `docs/decisions.md`.
2. Inspect repo before changing; RG-001 is first ticket. Do not assume Unity/Godot project already exists.
3. Read root `AGENTS.md` and `agent_pool/README.md`. Select a free ready task via pool CLI, queue it, add exact write paths/branch to its brief, then claim. Do not write status separately into Markdown.
4. Use isolated branch/worktree when possible. Keep commits focused; no unbounded dependency installations without explanation.
5. Verify using command or manual plan in task, save logs; update contract docs if relevant.
6. Transition `review` via pool CLI only with a nonempty evidence file. Reviewer moves to `done` via approve only with an acceptance review. The tool checks references and transitions; reviewer verifies evidence content.
7. Never silently bypass minimum Intel UHD support, external DLL physics plugin gate, or license gate.
8. No arbitrary third-party DLL execution by default. Plugins run with user's privileges and must be trusted.
9. Don't conflate handling JSON tuning with swappable algorithm DLL.
10. Avoid claims of 60 FPS on RX 470 / 30 FPS on Intel UHD until measured in packaged player.

11. Actual local target is i3-1215U, not the draft's i3-1315U. RX 470 is unavailable; no invented results.
12. Minimal DLL load proof is required before engine lock, production loader follows on the final SDK. No arbitrary DLL crash isolation claim.
13. Keep checkpoints in `agent_pool/agents/<agent>/memory/MEMORY.md`; save results/logs to outbox/logs. Do not implicitly execute the next ticket.
