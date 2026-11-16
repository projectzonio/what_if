"""python -X utf8 -I tools/knowledge/test_loop_stats.py"""
import importlib.util
import os
import unittest

spec = importlib.util.spec_from_file_location("loop_stats", os.path.join(os.path.dirname(os.path.abspath(__file__)), "loop-stats.py"))
ls = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ls)


def req(stop="tool_use"):
    return {"t": "req", "ts": None, "end": None, "model": "m", "stop": stop, "tools": [], "usage": {}}


def user(kind):
    return {"t": "user", "kind": kind, "ts": None}


class UserKind(unittest.TestCase):
    def test_kinds(self):
        self.assertEqual(ls.user_kind({"content": "ahoj"}), "prompt")
        self.assertEqual(ls.user_kind({"content": "<task-notification> x"}), "notification")
        self.assertEqual(ls.user_kind({"content": [{"type": "text", "text": "<task-notification>"}]}), "notification")
        self.assertEqual(ls.user_kind({"content": [{"type": "tool_result", "content": "x"}]}), "tool_result")
        self.assertEqual(ls.user_kind({"content": "<system-reminder>x"}), "meta")


class Loops(unittest.TestCase):
    def test_end_turn_and_wakes_split_loops(self):
        events = [user("prompt"), req(), user("tool_result"), req("end_turn"), user("notification"), req("end_turn"), user("notification"), req(), user("prompt"), req("end_turn")]
        loops = ls.loops_of(events)
        self.assertEqual([len(l["reqs"]) for l in loops], [2, 1, 1, 1])
        self.assertEqual([l["trigger"] for l in loops], ["prompt", "notification", "notification", "prompt"])

    def test_tool_results_do_not_split(self):
        events = [user("prompt"), req(), user("tool_result"), req(), user("meta"), req("end_turn")]
        self.assertEqual([len(l["reqs"]) for l in ls.loops_of(events)], [3])


if __name__ == "__main__":
    unittest.main()
