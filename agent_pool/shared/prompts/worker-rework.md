Rework $ticket podle $brief (sekce "Prepared handoff" a případný oddíl rework / review nálezů). Worktree $worktree. Ticket je už vrácený do in_progress (`pool.py rework`) na stejném jobu; queue ani claim nespouštěj. Orchestrátor do větve sloučil main, takže brief s novým oddílem máš ve worktree.
Postup a pasti: skill `wi-worker`. Zdroje čti odzadu (`research/gpt/final_for_orchestrator.md` první), každé tvrzení se zdrojem, čísla jsou "návrh, nezměřeno".

Oprav jen nálezy z review, rozsah nerozšiřuj. Rutinní kroky nepotvrzuj (PD-001). Předchozí verdikt uživatele nebo orchestrátora cituj doslova v outboxu.

Přetrhávej loop, nepřetržený loop je to nejdražší: běh nad ~2 min spusť s `run_in_background: true` (jen skutečný příkaz, žádné `&`, nohup, Start-Process, Start-Job, žádné čekání na stdin) a ukonči tah jednou větou; výstup do logu a po probuzení čti log. Kontroly před review (check, hygiene) v popředí s `timeout: 600000`. Nikdy nečekej v kuse déle než 45 min (`wait-run.ps1 -TimeoutS 2700`, po exit 2 čekání zopakuj). Detail: skill `wi-worker`, oddíl Break the loop.

$ask_user
$parallel_note
pool.py review spusť až na úplném konci.
