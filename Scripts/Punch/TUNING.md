# Punch system (M1) — architecture & tuning guide

The world recognizes punches in real time and judges their form. Everything
runs locally on each client for `Networking.LocalPlayer` (labels, sound and
haptics are inherently local; the station game stays synced as before).

## Pipeline

```
BoxStation.Update
  └─ PunchSystem.UpdateLocal(localPlayer)          (static helper class)
       ├─ MotionRing x3 sample hand/head positions + velocities (ring buffer, 128 samples)
       ├─ SweepHand: swept sphere (fist radius, substepped at radius/2 along the
       │    prev->cur segment) vs registered hittable colliders  → no tunneling
       │    per-hand cooldown + per-target latch (released when the fist exits)
       │    → one swing = exactly one hit; speed < minPunchSpeed = not a punch
       ├─ on hit: PunchFormValidator.Evaluate over the history window
       │    → punch record (speed, yaw, guard, flags, quality, score)
       └─ Dispatch: pad reacts (squash + BoxPad.NotifyPunchHit for the game pads)
            PunchFeedback: controller haptic (amplitude ∝ speed)
            PunchDebugPanel: append measured values
            retraction verdict is finalized ≤ retractWindow later and the panel updates
```

Implementation note: this UdonSharp version cannot instantiate user classes or
call their instance methods, and blocks `List<T>`/`Dictionary<T,K>`, so the
`Punch/*` files are **static helper classes** and all mutable state (ring
buffers, targets, latches, records) lives in `[NonSerialized]` array
fields on `BoxStation`. No GameObjects are created at runtime: the test wall
and the debug panel are pre-placed in the scene.

Test wall (pre-placed `Wall` child of `BoxStation`, station-local space;
player stands on −x side facing +x, so player-right = −z):

| pad        | local pos          | expected hand | notes                          |
|------------|--------------------|---------------|--------------------------------|
| Jab        | (0.55, 1.25, 0)    | any           | straight ahead                 |
| CrossL     | (0.55, 1.25, +0.45)| right         | player's left pad              |
| CrossR     | (0.55, 1.25, −0.45)| left          | player's right pad (left hand hits the right pad) |
| HookL      | (0.55, 1.55, +0.35)| right         | temple height                  |
| HookR      | (0.55, 1.55, −0.35)| left          | temple height                  |
| UpperCut   | (0.55, 0.90, 0)    | any           | low center                     |

The two existing game pads (`PadL`/`PadR`) are also registered as hittable
(`GamePad` targets, inactive pads are skipped).

## Form checks (per punch, from the pre-impact history window)

- **Hip drive** — signed head yaw over `hipWindow` must reach `hipDriveMinDeg`,
  and (for lateral punches) rotate *into* the punch direction
  (`hipRequireDirection`). No rotation ⇒ "arm punch" ⇒ `noHipDrive`.
- **Guard** — the other hand must be within `guardMaxDist` of the chin during
  `guardWindow` before impact, else `guardDown`.
- **Telegraph** — during `telegraphWindow` (excluding the final
  `telegraphExcludeFinal` of acceleration), the fist must not move backward
  along the punch axis by ≥ `telegraphMinDist` nor dip by ≥ `telegraphDip`.
  Telegraphed ⇒ quality `Invalid`, score 0.
- **Retraction** — within `retractWindow` after impact the fist must return to
  within `retractMaxDist` of the chin or move ≥ `retractMinBack` back along the
  punch axis, else `noRetraction`.

Quality: `Invalid` if telegraphed; else `Clean` with 0 faults, `Sloppy` with ≥1
of {noHipDrive, guardDown, noRetraction}. Score = speed × 1.0 (clean) / 0.6
(sloppy) / 0 (invalid).

## Thresholds (initial values — tune by playtesting!)

All live as public fields on `BoxStation` (edit in the inspector, or change the
defaults in `BoxStation.cs`). Watch them live in the debug panel header.

| field                    | default | meaning |
|--------------------------|---------|---------|
| `minPunchSpeed`          | 2.0 m/s | below this a touch is ignored |
| `fistRadius`             | 0.10 m  | swept-sphere radius of the fist |
| `hitCooldown`            | 0.25 s  | per-hand; one swing = one hit |
| `hipWindow`              | 0.30 s  | head-yaw history for hip drive |
| `hipDriveMinDeg`         | 10°     | rotation required into the punch |
| `hipRequireDirection`    | true    | yaw sign must match punch side |
| `guardWindow`            | 0.08 s  | window around impact for guard check |
| `guardMaxDist`           | 0.28 m  | other-hand distance to chin |
| `telegraphWindow`        | 0.45 s  | wind-up scan window |
| `telegraphExcludeFinal`  | 0.12 s  | final acceleration excluded |
| `telegraphMinDist`       | 0.12 m  | pull-back along axis = telegraph |
| `telegraphDip`           | 0.08 m  | dip before striking = telegraph |
| `retractWindow`          | 0.35 s  | time allowed to return to guard |
| `retractMaxDist`         | 0.32 m  | hand-to-chin distance that counts as retracted |
| `retractMinBack`         | 0.12 m  | backward travel along axis that counts |
| `maxSpeedForFeedback`    | 8 m/s   | speed mapping to full haptic |
| `sloppyScoreMult`        | 0.6     | score multiplier for sloppy punches |
| `debugPanelLines`        | 8       | rows kept on the debug panel |

### How to tune

1. Build/upload the world, put it on Quest (or PC VR).
2. The debug panel (`PunchDebugPanel`, above the test wall) shows the last 8
   punches: hand, target, speed, signed yaw, guard distance, pullback, flags,
   quality, score. Toggle with `P` on desktop or `BoxStation.ToggleDebugPanel()`.
3. Punch each target deliberately: clean jab/cross/hook/uppercut, then each
   fault on purpose (arm-only punch, other hand down, big wind-up, leave the
   hand out after impact).
4. Read the measured values on the panel and adjust the matching threshold on
   `BoxStation` until clean punches stay flag-free and each fault triggers
   reliably. Record the final values here.

### Tuned values (fill in after playtesting)

| field | tuned value | notes |
|-------|-------------|-------|
|       |             |       |

## Known limits (M1)

- Torso rotation is inferred from head yaw — fine standing, imperfect seated.
- Fist/knuckle orientation is not checked (controller rotation is not a
  reliable proxy) — future addition.
- Uppercut pad disables the telegraph check entirely: a proper uppercut dips
  toward a low pad, which is indistinguishable from a wind-up with the current
  axis-relative metric. Retraction is still checked.
- Feedback (haptics/pad squash) is local to the punching client; only the
  station game state is networked. Floating labels and impact sound are
  intentionally not used yet. Best-score persistence, pods and the arena
  are out of scope for M1.
- Desktop (non-VR) play has no tracked bones, so the pipeline is inert there;
  test in VR.
