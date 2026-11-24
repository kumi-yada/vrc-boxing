---
name: trello-vrc-boxing
description: "Card-scope rule for Trello: when the user asks about cards/tasks/to-dos, ONLY look at cards in the \"VRC Boxing\" list on the \"Main\" board — nothing else. Never read, search, or list cards on any other board, any other list (including other lists on Main), my-cards, or the Inbox, unless the user explicitly names that other location. Load this before any Trello card lookup."
---

# Trello card scope: VRC Boxing list on Main board only

When the user asks anything about Trello cards (tasks, to-dos, boxing todos, "what's on my plate", etc.), the ONLY place to look is:

- **Board:** `Main` — https://trello.com/b/hI2umb97/main (board id `63bbfa7c1503ea00e5d457f0`)
- **List:** `VRC Boxing` on that board (list id `6581506fee1804d0761cbc80`)

## Rules

- Read cards only from the `VRC Boxing` list on the `Main` board. Do not open any other list on `Main` (Notes, Recipies, Due, Games, VRCSolitaire), any other board, `get_my_cards`, Trello search across boards, or the Inbox for card content.
- Do not assume cards exist anywhere else. If the user asks "find the card about X", search only within `VRC Boxing` on `Main`.
- Exception: only if the user explicitly names a different board or list, use that location instead.
- Load `trello-use` first for the ARI id format and read-tool rules; get board/list ARIs from read tools, never construct them by hand.
