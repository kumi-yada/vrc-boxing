# Punch detection

Each hittable object (the bag, the two game pads) carries a `PunchTarget`
component and detects hits on its own — there is no central punch system.

## Pipeline (per client, for `Networking.LocalPlayer` only)

1. `PunchTarget.Update` samples both hand bones into a small ring buffer.
2. Swept sphere along the prev->cur fist segment (substepped at half the fist
   radius, so no tunneling) vs the target's own collider. Gates:
   `minPunchSpeed`, per-hand `hitCooldown`, and a per-hand latch released when
   the fist exits the target — one swing = exactly one hit.
3. On hit, the fist travel over the last `classifyWindow` seconds is classified
   by `PunchClassifier.Classify`:
   - mostly upward             -> Uppercut
   - horizontal, toward target -> Jab
   - horizontal, from the side -> Hook
4. Reaction: squash animation + haptic pulse on the puncher, and the last
   punch is written to the target's `readout` text (the debug panel above the
   wall; toggle with `P`).

All detection is local to the punching client; nothing about punches is
synced. Desktop play has no tracked bones, so detection is inert there — test
in VR.

## Tunables (fields on each `PunchTarget`)

| field            | default | meaning                                        |
|------------------|---------|------------------------------------------------|
| `fistRadius`     | 0.10 m  | swept-sphere radius of a fist                  |
| `minPunchSpeed`  | 0.5 m/s | below this a touch is not a punch              |
| `hitCooldown`    | 0.25 s  | per hand; one swing = one hit                  |
| `classifyWindow` | 0.15 s  | fist travel window used to classify the punch  |
| `historySize`    | 12      | ring-buffer samples kept per hand              |

Classification thresholds live in `PunchClassifier` (`UppercutMinUp`,
`JabMinForward`).
