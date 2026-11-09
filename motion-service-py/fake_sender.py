"""Synthetic player: sends a scripted skeleton over Motion Protocol v1, so the Unity side can be tested
without standing in front of a camera.

Scripts:
    basic  idle, step left/right, jump, crouch, hands up   (Runner gestures)
    bowl   hands up (start), three underhand bowling swings, hands up again (must NOT count as a throw)
    slash  hands up (start), six fast right-hand swipes across the body   (Fruta)
    idle   a fidgeting player (sway, weight shift, scratching head, small knee bend, tiptoe): must trigger NOTHING
    dropout  calibrate, camera loses the player for 1 s, player is back, steps left: must NOT recalibrate
    jumps  real jumps (knees bend first, then take-off, landing): hop, small hop, big jump; then a duck and a fast
           stand-up. Must give 3 jumps (the dips must not turn them into crouches) and the stand-up must NOT be a jump
    runner  plays Vrapo! for a visual check: hands up to start, countdown, then sidesteps, hops and ducks
    paint  Pikturo!: picks a colour on the right edge (hold), then paints with the right hand while the left hand is
           raised as the switch: a circle, then (switch down) moves to a new spot and paints a line
    sword  Shpata! (sword right hand, shield left hand): hands up, waits for the knight, swings at its guard, then
           holds the shield overhead, left and right, each followed by a counterattack, then lets one blow through
    brawl  Fluturo!: fists up, then jabs left and right without a break (~3 a second) for 2.5 minutes, stepping side
           to side (a long run through rushes and clashes)
    fly    Fluturo!: hands up, fists up in the countdown, then jabs straight at the camera (right, left), flies left and
           punches, flies right and punches, ducks, jumps, a hook and a jab, fists up all along. Must give exactly 6
           punches (the rest gives none)
    turns  Fluturo!: hands up, countdown, a 5 s step right (turns right at a crossroads), back, a 5 s step left
    dress  Vishu!: holds the left hand on a category and the right hand on an item, five times (a ball gown, a princess
           top, long hair, a crown, fairy wings), presses the wings again (a new colour) and the magic button, then
           sways, steps to each side and raises both arms (the clothes must follow)
    runway Vishu!'s fashion show poses, over and over: star, hands on hips, heart above the head (2.5 s each)
    turn   Vishu!: turns 45 then 90 degrees to the screen's right, back, 60 degrees to the left, back, then spins
           slowly all the way round (the clothes must wrap around the body, never fold or flip)
    tennis_move  Tenis!: hands up, the match intro, then steps: to the right, back, to the left, back, toward the camera
           and away from it (the player must stay on the baseline: only sideways steps move it), back
    tennis Tenis!: hands up, the match intro, then a racket swing of the right arm every 3.4 s, its fast part always
           1.6 s into the segment (serve, forehand, backhand, forehand, lob, backhand, slice, forehand, a swing straight
           up, three times). Wind-ups and the way back are slow (under the swing speed), so each segment is exactly one
           swing: forehands sweep from the right across the body, backhands from the left, at chest height (where the
           practice feeds arrive), the lob scoops up across (~43 degrees), the slice chops down across, the last one
           goes straight up (since 2026-11-07 a swing up at a ball in reach lobs it; only a strong one down misses)
    tennis_flick  Tenis! played the lazy way (2026-11-06: "like a Wii remote, moving the whole arm is tiring"): the same
           3.4 s segments, but every stroke is a small flick of the forearm, ~15 cm (a third of a torso) in 0.22 s,
           gentle: forehand and backhand flicks in front of the body, a serve of two flicks (up: the toss, down: the hit),
           and a small flick straight up (never a miss: at a ball in reach it lobs, else it's nothing). With
           --glitches it also shows the pose model's glitches (see there): they must not swing
    place  the placement check (Vrapo!): stands too close (head out of the picture) and raises the hands (must not
           start), steps back (calibrates), stands at the screen's left (no room for a lane step: "move right", hands
           up must not start), back to the middle, starts, then drifts to the left edge during the run (the warning)

    sp_run  Sporte!'s 100 m: stands still through the start (no false start), then runs on the spot (knees up, arms
           pumping, ~3.4 steps a second) for 22 s
    sp_kick  Sporte!'s football: a right-foot kick every 0.7 s (a pass is met whenever it comes), with rests

    volley  Volejboll!: hands up (start), the match intro, then 10 s cycles: both hands together low in front (the bump,
           0.2-1.9 s), both hands up (the set, 2.3-3.4 s), the right arm raised slowly and swung down (the spike, its fast
           part at 4.4 s), both hands up again (the block, 7.6-8.9 s). The first cycle serves instead of spiking: the swing
           at 4.4 s and again at 6.4 s (one of them comes while the toss is up). VolleyGameTests times the computer's balls
           to the cycle in practice: its attacks at 8.3 s, a smash (blocked) and a free ball (received) by turns
    volley_duo  Volejboll! for two: both calibrate and raise their hands, then each holds the bump, then the set, then
           spikes, in 6 s cycles, player 2 three seconds after player 1 (the poses are held long: balls the computer doesn't
           time still find a move)

While the game asks for camera frames (Pikturo's camera mode), it also sends a drawn "camera picture" of the room
and the player, exactly where the joints are (video.py), so the camera mode can be checked without a camera.

    python fake_sender.py                      # basic, loops forever
    python fake_sender.py --script bowl --once # one pass of the bowling script

--glitches N adds the box's pose-model glitches (measured 2026-11-06), N a second on average: a wrist flipped to
another spot 15-50 cm away for one or two frames, or pulled most of the way onto its elbow for one to three frames,
less sure while it lasts (visibility 0.7-0.85).
"""

import argparse
import math
import random
import time

from protocol import FpsMeter, UdpSender, build_packet, now_ms
from skeleton import J, JOINT_NAMES, Person
from video import CONTROL_PORT, VideoStreamer

WIDTH, HEIGHT = 1280, 720
VIEW_HEIGHT_M = 2.6                          # metres of the scene visible vertically at the player's distance
VIEW_WIDTH_M = VIEW_HEIGHT_M * WIDTH / HEIGHT

# Standing 1.7 m player, metres. X > 0 = image right = the player's LEFT side (camera faces the player).
STAND = {
    "nose": (0.00, 1.60), "left_eye": (0.03, 1.64), "right_eye": (-0.03, 1.64),
    "left_ear": (0.07, 1.62), "right_ear": (-0.07, 1.62),
    "left_shoulder": (0.19, 1.42), "right_shoulder": (-0.19, 1.42),
    "left_elbow": (0.24, 1.12), "right_elbow": (-0.24, 1.12),
    "left_wrist": (0.26, 0.85), "right_wrist": (-0.26, 0.85),
    "left_hip": (0.11, 0.95), "right_hip": (-0.11, 0.95),
    "left_knee": (0.12, 0.52), "right_knee": (-0.12, 0.52),
    "left_ankle": (0.12, 0.08), "right_ankle": (-0.12, 0.08),
}
HANDS_UP = {"left_elbow": (0.22, 1.66), "right_elbow": (-0.22, 1.66),
            "left_wrist": (0.18, 1.95), "right_wrist": (-0.18, 1.95)}
UPPER_BODY = {"nose", "left_eye", "right_eye", "left_ear", "right_ear", "left_shoulder", "right_shoulder",
              "left_elbow", "right_elbow", "left_wrist", "right_wrist"}

# (name, duration s, target state). State: x = sideways offset (m, + = player's left), crouch 0..1, hands 0..1.
# "jump" plays a jump arc during the segment; "arm" animates the right arm (see ARM_MOTIONS).
IDLE = dict(x=0.0, crouch=0, hands=0)
BASIC = [
    ("idle (calibration)", 2.5, dict(x=0.0, crouch=0, hands=0)),
    ("step LEFT", 1.6, dict(x=0.40, crouch=0, hands=0)),
    ("back to center", 1.6, dict(x=0.0, crouch=0, hands=0)),
    ("step RIGHT", 1.6, dict(x=-0.40, crouch=0, hands=0)),
    ("back to center", 1.6, dict(x=0.0, crouch=0, hands=0)),
    ("JUMP", 1.4, dict(x=0.0, crouch=0, hands=0, jump=True)),
    ("CROUCH", 1.4, dict(x=0.0, crouch=1, hands=0)),
    ("stand up", 1.2, dict(x=0.0, crouch=0, hands=0)),
    ("HANDS UP", 1.4, dict(x=0.0, crouch=0, hands=1)),
    ("hands down", 1.2, dict(x=0.0, crouch=0, hands=0)),
]
START = [("HANDS UP (start / confirm)", 1.4, dict(IDLE, hands=1)), ("hands down", 1.0, IDLE)]
BOWL = [
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("BOWL 1", 1.8, dict(IDLE, arm="bowl")),
    ("pause", 0.8, IDLE),
    ("BOWL 2", 1.8, dict(IDLE, arm="bowl")),
    ("pause", 0.8, IDLE),
    ("BOWL 3", 1.8, dict(IDLE, arm="bowl")),
    ("pause", 0.8, IDLE),
    ("HANDS UP (not a throw)", 1.4, dict(IDLE, hands=1)),
    ("hands down", 1.2, IDLE),
]
SLASH = [
    ("idle (calibration)", 2.5, IDLE),
    *START,
    *[("SWIPE", 1.2, dict(IDLE, arm="swipe")), ("pause", 0.4, IDLE)] * 6,
]
IDLE_FIDGET = [
    ("idle (calibration)", 2.5, IDLE),
    ("sway", 4.0, dict(IDLE, move="sway")),
    ("weight shift", 1.5, dict(IDLE, x=0.12)),
    ("weight back", 1.5, IDLE),
    ("scratch head", 2.5, dict(IDLE, arm="scratch")),
    ("small knee bend", 1.0, dict(IDLE, crouch=0.15)),
    ("stand", 1.0, IDLE),
    ("tiptoe", 2.0, dict(IDLE, move="tiptoe")),
    ("arms crossed", 2.5, dict(IDLE, arm="cross")),
    ("idle", 1.5, IDLE),
]
DROPOUT = [
    ("idle (calibration)", 2.5, IDLE),
    ("idle", 1.0, IDLE),
    ("CAMERA LOSES PLAYER", 1.0, dict(IDLE, absent=True)),
    ("back in view", 1.0, IDLE),
    ("step LEFT", 1.6, dict(IDLE, x=0.40)),
    ("back to center", 1.2, IDLE),
]
JUMPS = [
    ("idle (calibration)", 2.5, IDLE),
    ("HOP (knees dip 12 cm)", 1.6, dict(IDLE, hop=(0.12, 0.40))),
    ("SMALL HOP (dip 8 cm)", 1.6, dict(IDLE, hop=(0.08, 0.30))),
    ("BIG JUMP (dip 20 cm)", 1.8, dict(IDLE, hop=(0.20, 0.45))),
    ("CROUCH (duck and hold)", 1.4, dict(IDLE, crouch=0.95)),
    ("stand up fast", 1.2, IDLE),
]
RUNNER = [  # plays Vrapo!: start with hands up, wait out the countdown, then dodge left/right, hop and duck
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("countdown", 3.2, IDLE),
    *[("step LEFT", 1.1, dict(IDLE, x=0.40)), ("HOP", 1.2, dict(IDLE, x=0.40, hop=(0.12, 0.40))),
      ("back to center", 0.9, IDLE), ("DUCK", 1.0, dict(IDLE, crouch=0.95)), ("stand", 0.8, IDLE),
      ("step RIGHT", 1.1, dict(IDLE, x=-0.40)), ("HOP", 1.2, dict(IDLE, x=-0.40, hop=(0.12, 0.40))),
      ("back to center", 0.9, IDLE)] * 2,
]
PAINT = [  # Pikturo!: hand targets are given in screen ("zone") coordinates, see zone_hand()
    ("idle (calibration)", 2.5, IDLE),
    ("CHOOSE A COLOUR (right hand holds on the right edge)", 2.6, dict(IDLE, arm="paint_pick")),
    ("PAINT A CIRCLE (right hand paints while the left hand is raised)", 4.6, dict(IDLE, arm="paint_circle")),
    ("MOVE (switch down), THEN PAINT A LINE", 4.0, dict(IDLE, arm="paint_line")),
    ("idle", 1.0, IDLE),
]
SWORD = [  # Shpata!: a swing at the knight's guard (bounces off), then three shield blocks, each with a counterattack
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("countdown, the knight walks in", 5.8, IDLE),
    ("SWING AT ITS GUARD (bounces off its shield)", 1.3, dict(IDLE, arm="cut_right")), ("pause", 0.5, IDLE),
    ("SHIELD OVERHEAD (left hand above the head)", 1.8, dict(IDLE, arm="shield_over")),
    ("COUNTERATTACK", 1.3, dict(IDLE, arm="cut_left")), ("pause", 0.5, IDLE),
    ("SHIELD LEFT (left hand out on the screen's left)", 1.8, dict(IDLE, arm="shield_left")),
    ("COUNTERATTACK", 1.3, dict(IDLE, arm="cut_down")), ("pause", 0.5, IDLE),
    ("SHIELD RIGHT (left hand across to the screen's right)", 1.8, dict(IDLE, arm="shield_right")),
    ("COUNTERATTACK", 1.3, dict(IDLE, arm="cut_right")), ("pause", 0.5, IDLE),
    ("NO BLOCK (arms down while it attacks)", 2.5, IDLE),
    ("idle", 1.0, IDLE),
]
GUARD = dict(IDLE, arm="guard")   # both fists up by the chin, like a boxer (raising them may count as a punch)
FLY = [  # Fluturo!: fists up in the countdown, then jabs straight at the camera (only world joints show them), flying
    # left/right, a duck and a jump, all with the fists kept up
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("countdown, fists up", 3.4, dict(IDLE, arm="raise_guard")),
    ("PUNCH RIGHT (straight at the TV)", 1.1, dict(IDLE, arm="guard_jab_right")),
    ("PUNCH LEFT", 1.1, dict(IDLE, arm="guard_jab_left")),
    ("step LEFT (fly left)", 1.3, dict(GUARD, x=0.40)),
    ("PUNCH RIGHT (flying left)", 1.1, dict(IDLE, x=0.40, arm="guard_jab_right")),
    ("back to the middle", 1.2, GUARD),
    ("step RIGHT (fly right)", 1.3, dict(GUARD, x=-0.40)),
    ("PUNCH LEFT (flying right)", 1.1, dict(IDLE, x=-0.40, arm="guard_jab_left")),
    ("back to the middle", 1.2, GUARD),
    ("DUCK (fly low)", 1.3, dict(GUARD, crouch=0.8)),
    ("stand", 1.0, GUARD),
    ("JUMP (fly up)", 1.4, dict(GUARD, hop=(0.12, 0.40))),
    ("HOOK RIGHT", 1.1, dict(IDLE, arm="guard_hook_right")),
    ("PUNCH RIGHT", 1.1, dict(IDLE, arm="guard_jab_right")),
    ("idle", 4.0, GUARD),
]
BRAWL = [  # Fluturo!: fists up, then jabs left and right without a break (about 3 a second), stepping side to side
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("countdown, fists up", 3.4, dict(IDLE, arm="raise_guard")),
    *[seg for k in range(6) for seg in (("FLURRY", 8.0, dict(IDLE, arm="flurry")),
                                        ("FLURRY stepping right", 8.0, dict(IDLE, x=-0.3, arm="flurry")),
                                        ("FLURRY stepping left", 8.0, dict(IDLE, x=0.3, arm="flurry")))],
    ("idle", 2.0, GUARD),
]
TURNS = [  # Fluturo!: a long step right (the hero turns right at the next crossroads), back, a long step left
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("countdown", 3.4, IDLE),
    ("step RIGHT and stay (turn right)", 5.0, dict(IDLE, x=-0.40)),
    ("back to the middle", 2.0, IDLE),
    ("step LEFT and stay (turn left)", 5.0, dict(IDLE, x=0.40)),
    ("back to the middle", 2.0, IDLE),
]
DRESS = [  # Vishu!: hand targets are offsets from the shoulders in torso lengths on the mirrored screen, see dress_hand()
    ("idle (calibration, the camera picture starts)", 3.0, IDLE),
    ("LEFT HAND ON 'SKIRTS', RIGHT HAND ON THE BALL GOWN", 3.4, dict(IDLE, arm="dress_skirt")),
    ("LEFT HAND ON 'TOPS', RIGHT HAND ON THE PRINCESS TOP", 3.4, dict(IDLE, arm="dress_top")),
    ("LEFT HAND ON 'HAIR', RIGHT HAND ON LONG HAIR", 3.4, dict(IDLE, arm="dress_hair")),
    ("LEFT HAND ON 'HEADWEAR', RIGHT HAND ON THE CROWN", 3.4, dict(IDLE, arm="dress_head")),
    ("LEFT HAND ON 'WINGS', RIGHT HAND ON FAIRY WINGS, AGAIN (A NEW COLOUR)", 5.2, dict(IDLE, arm="dress_wings")),
    ("LEFT HAND ON 'MAGIC'", 2.4, dict(IDLE, arm="dress_magic")),
    ("sway", 3.0, dict(IDLE, move="sway")),
    ("step LEFT", 1.4, dict(IDLE, x=0.35)),
    ("step RIGHT", 1.8, dict(IDLE, x=-0.35)),
    ("back to the middle, ARMS UP AND OUT", 2.4, dict(IDLE, arm="dress_star")),
    ("idle", 1.5, IDLE),
]
TURN = [  # Vishu!: degrees, + = the chest toward the screen's right (the screen-right shoulder goes back)
    ("idle (calibration, the camera picture starts)", 3.0, IDLE),
    ("TURN 45 deg to the screen's right", 3.0, dict(IDLE, turn=(0, 45))),
    ("TURN 90 deg (a profile)", 3.0, dict(IDLE, turn=(45, 90))),
    ("back to the front", 2.0, dict(IDLE, turn=(90, 0))),
    ("TURN 60 deg to the screen's left", 3.0, dict(IDLE, turn=(0, -60))),
    ("back to the front", 2.0, dict(IDLE, turn=(-60, 0))),
    ("SPIN slowly all the way round", 8.0, dict(IDLE, turn=(0, 360), turn_s=7.0)),
    ("idle", 1.5, IDLE),
]
RUNWAY = [  # Vishu!'s runway poses in a loop (the game asks for them in any order)
    ("STAR (arms up and out)", 2.5, dict(IDLE, arm="pose_star")), ("rest", 0.5, IDLE),
    ("HANDS ON HIPS", 2.5, dict(IDLE, arm="pose_hips")), ("rest", 0.5, IDLE),
    ("HEART ABOVE THE HEAD", 2.5, dict(IDLE, arm="pose_heart")), ("rest", 0.5, IDLE),
]
TENNIS = [  # Tenis!: one swing per 3.4 s segment, the fast part 1.6 s in (TennisGameTests times the computer's feeds to it)
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("match intro", 3.4, IDLE),
    *[seg for _ in range(3) for seg in (
        ("SERVE (overhead, down)", 3.4, dict(IDLE, arm="tennis_serve")),
        ("FOREHAND", 3.4, dict(IDLE, arm="tennis_forehand")),
        ("BACKHAND", 3.4, dict(IDLE, arm="tennis_backhand")),
        ("FOREHAND", 3.4, dict(IDLE, arm="tennis_forehand")),
        ("LOB (slow, upward)", 3.4, dict(IDLE, arm="tennis_lob")),
        ("BACKHAND", 3.4, dict(IDLE, arm="tennis_backhand")),
        ("SLICE (chop down)", 3.4, dict(IDLE, arm="tennis_slice")),
        ("FOREHAND", 3.4, dict(IDLE, arm="tennis_forehand")),
        ("UP (straight up: a lob)", 3.4, dict(IDLE, arm="tennis_up")))],
]
TENNIS_FLICK = [  # Tenis! with small flicks of the forearm (gentle swings), the fast part 1.6 s into each 3.4 s segment
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("match intro", 3.4, IDLE),
    *[seg for _ in range(3) for seg in (
        ("FLICK SERVE (a flick up tosses, a flick down hits)", 3.4, dict(IDLE, arm="flick_serve")),
        ("FOREHAND FLICK", 3.4, dict(IDLE, arm="flick_forehand")),
        ("BACKHAND FLICK", 3.4, dict(IDLE, arm="flick_backhand")),
        ("FOREHAND FLICK", 3.4, dict(IDLE, arm="flick_forehand")),
        ("BACKHAND FLICK", 3.4, dict(IDLE, arm="flick_backhand")),
        ("FOREHAND FLICK", 3.4, dict(IDLE, arm="flick_forehand")),
        # last, like the tennis script's swing straight up: never an instant miss
        ("SMALL FLICK UP (never a miss)", 3.4, dict(IDLE, arm="flick_up")))],
]

TENNIS_MOVE = [  # Tenis!: steps to the sides, toward the camera (`near` = metres) and away: none may move the players (they run by themselves)
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("match intro", 3.6, IDLE),
    ("step RIGHT (the player's right: the screen's right)", 2.5, dict(IDLE, x=-0.4)),
    ("back to the middle", 2.0, IDLE),
    ("step LEFT", 2.5, dict(IDLE, x=0.4)),
    ("back to the middle", 2.0, IDLE),
    ("step TOWARD the camera (the player stays on the baseline)", 2.5, dict(IDLE, near=0.5)),
    ("back to the middle", 2.0, IDLE),
    ("step AWAY from the camera (still the baseline)", 2.5, dict(IDLE, near=-0.4)),
    ("back to the middle", 2.0, IDLE),
]
PLACE = [  # the placement check, in Vrapo!: too close, then too near a side, then a run drifting to the edge
    ("TOO CLOSE (the head out of the picture): 'step back', no calibration", 4.0, dict(IDLE, near=1.3)),
    ("hands up while too close: must NOT start", 1.6, dict(IDLE, near=1.3, hands=1)),
    ("hands down", 1.0, dict(IDLE, near=1.3)),
    ("step back: calibrates", 2.5, IDLE),
    ("NEAR THE SCREEN'S LEFT (no room for a lane step): 'move right'", 3.0, dict(IDLE, x=1.8)),
    ("hands up there: must NOT start", 1.6, dict(IDLE, x=1.8, hands=1)),
    ("hands down", 1.0, dict(IDLE, x=1.8)),
    ("back to the middle", 2.0, IDLE),
    *START,
    ("countdown", 3.5, IDLE),
    ("RUN: drift to the screen's left edge (warning arrows)", 3.0, dict(IDLE, x=2.0)),
    ("back to the middle", 2.5, IDLE),
]
SP_RUN = [  # Sporte!'s 100 m: still through "on your marks" and "set", then running on the spot
    ("idle (calibration)", 2.5, IDLE),
    ("wait for the gun (still)", 9.0, IDLE),
    ("RUN ON THE SPOT", 22.0, dict(IDLE, arm="run_spot")),
    ("idle", 2.0, IDLE),
]
SP_KICK = [  # Sporte!'s football: kick after kick (each pass is met), rests while keeping goal
    ("idle (calibration)", 2.5, IDLE),
    ("countdown", 3.0, IDLE),
    *[seg for _ in range(3) for seg in (
        ("KICKS with the right foot", 5.0, dict(IDLE, arm="kick_loop")),
        ("keeping goal: arms out", 5.5, dict(IDLE, arm="arms_out")))],
]
VOLLEY = [  # Volejboll!: 10 s cycles of bump, set, spike and block (VolleyGameTests times the practice balls to them)
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("match intro", 3.4, IDLE),
    ("BUMP, SET, then SERVE twice (the toss is up for one of them), BLOCK", 10.0, dict(IDLE, arm="volley_serve_cycle")),
    *[("BUMP (hands together low), SET (both hands up), SPIKE (one arm down), BLOCK (both hands up)", 10.0,
       dict(IDLE, arm="volley_cycle")) for _ in range(8)],
]
# Two players (a segment's target is a pair: player 1, player 2). Player 1 stands DUO_X[0] to the image's right (the
# players' left: the screen's left in the mirrored game), player 2 DUO_X[1] to its left; each one's x, arm, ... are their
# own, as in the one-player scripts (x > 0 = that player's left).
DUO_X = (0.75, -0.75)
TENNIS_DUO = [  # Tenis! for two: TennisGameTests sets the mode (versus or together) before the hands go up
    ("both idle (calibration)", 3.0, (IDLE, IDLE)),
    ("both HANDS UP (start)", 1.4, (dict(IDLE, hands=1), dict(IDLE, hands=1))),
    ("hands down", 1.0, (IDLE, IDLE)),
    ("match intro", 3.4, (IDLE, IDLE)),
    ("player 1 steps RIGHT, player 2 still", 2.5, (dict(IDLE, x=-0.35), IDLE)),
    ("back to the middle", 1.5, (IDLE, IDLE)),
    ("player 2 steps RIGHT, player 1 still", 2.5, (IDLE, dict(IDLE, x=-0.35))),
    ("back to the middle", 1.5, (IDLE, IDLE)),
    *[seg for _ in range(4) for seg in (
        ("player 1 TOSSES AND SERVES (a flick up, then the swing down), player 2 still", 3.4,
         (dict(IDLE, arm="tennis_toss_serve"), IDLE)),
        ("player 2 FOREHAND, player 1 still", 3.4, (IDLE, dict(IDLE, arm="tennis_forehand"))))],
]
VOLLEY_DUO = [  # Volejboll! for two: VolleyGameTests sets the mode (together or versus) before the hands go up
    ("both idle (calibration)", 3.0, (IDLE, IDLE)),
    ("both HANDS UP (start)", 1.4, (dict(IDLE, hands=1), dict(IDLE, hands=1))),
    ("hands down", 1.0, (IDLE, IDLE)),
    ("match intro", 3.4, (IDLE, IDLE)),
    *[("player 1: bump, set, spike; player 2 the same 3 s later", 6.0,
       (dict(IDLE, arm="volley_duo_cycle"), dict(IDLE, arm="volley_duo_cycle_late"))) for _ in range(9)],
]
KART = [  # Gara!: both hands on an invisible wheel; 8 s cycles: turning left and right, a hard right turn held (a drift), both
    # hands up (the item), a jump at the start of each (KartGameTests watches what reaches the kart)
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("the countdown: both hands on the wheel, level", 4.2, dict(IDLE, arm="kart_level")),
    *[("steer left and right, a hard right turn (a drift), both hands up; a jump", 8.0, dict(IDLE, arm="kart_drive", hop=(0.1, 0.36)))
      for _ in range(17)],
]
KART_DUO = [  # Gara! for two: KartGameTests sets the mode (versus or together) before the hands go up
    ("both idle (calibration)", 3.0, (IDLE, IDLE)),
    ("both HANDS UP (start)", 1.4, (dict(IDLE, hands=1), dict(IDLE, hands=1))),
    ("hands down", 1.0, (IDLE, IDLE)),
    ("the countdown: both hold their wheels", 4.2, (dict(IDLE, arm="kart_level"), dict(IDLE, arm="kart_level"))),
    *[("both steer, player 2 half a cycle later", 8.0, (dict(IDLE, arm="kart_drive", hop=(0.1, 0.36)), dict(IDLE, arm="kart_drive_late")))
      for _ in range(5)],
]
SCRIPTS = {"basic": BASIC, "bowl": BOWL, "slash": SLASH, "idle": IDLE_FIDGET, "dropout": DROPOUT, "jumps": JUMPS,
           "runner": RUNNER, "paint": PAINT, "sword": SWORD, "fly": FLY, "turns": TURNS, "brawl": BRAWL, "dress": DRESS,
           "runway": RUNWAY, "turn": TURN, "tennis": TENNIS, "tennis_move": TENNIS_MOVE, "place": PLACE,
           "sp_run": SP_RUN, "sp_kick": SP_KICK, "tennis_duo": TENNIS_DUO,
           "tennis_flick": TENNIS_FLICK, "volley": VOLLEY, "volley_duo": VOLLEY_DUO, "kart": KART,
           "kart_duo": KART_DUO}
TRANSITION_S = 0.25
JUMP_HEIGHT_M = 0.35
JUMP_TIME_S = 0.55


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def minjerk(t):
    t = max(0.0, min(1.0, t))
    return 10 * t ** 3 - 15 * t ** 4 + 6 * t ** 5


def hop(t, dip, flight, start=0.2, dip_t=0.28, push_t=0.22, land_dip=0.10, land_t=0.35):
    """A real jump: knees dip, push up (fastest at take-off), ballistic flight, knees bend on landing.
    Returns (extra crouch 0..1, jump height m). Crouch 1 lowers the torso centre by ~0.41 m (see body_at)."""
    g = 9.81
    v_takeoff = g * flight / 2
    t -= start
    if t < 0:
        return 0.0, 0.0
    if t < dip_t:
        return dip * minjerk(t / dip_t) / 0.41, 0.0
    t -= dip_t
    if t < push_t:  # cubic Hermite: from -dip at rest to 0 at take-off speed
        s = t / push_t
        y = -dip * (2 * s ** 3 - 3 * s ** 2 + 1) + v_takeoff * push_t * (s ** 3 - s ** 2)
        return -y / 0.41, 0.0
    t -= push_t
    if t < flight:
        return 0.0, v_takeoff * t - 0.5 * g * t * t
    t -= flight
    if t < land_t:
        return land_dip * math.sin(math.pi * t / land_t) / 0.41, 0.0
    return 0.0, 0.0


def bowl_arm(t):
    """Underhand swing of the right arm: back (behind the body), fast forward, follow-through, return.
    Angle 0 = arm hanging, + = forward (toward the camera, negative z)."""
    if t < 0.5:
        deg = -60 * smoothstep(t / 0.5)
    elif t < 0.85:
        deg = -60 + 140 * smoothstep((t - 0.5) / 0.35)
    elif t < 1.3:
        deg = 80
    else:
        deg = 80 * (1 - smoothstep((t - 1.3) / 0.5))
    a = math.radians(deg)
    shoulder_y = STAND["right_shoulder"][1]
    return {
        "right_elbow": (-0.24, shoulder_y - 0.3 * math.cos(a), -0.3 * math.sin(a)),
        "right_wrist": (-0.26, shoulder_y - 0.6 * math.cos(a), -0.6 * math.sin(a)),
    }


def swipe_arm(t):
    """Right hand: wind up across the body, fast swipe out to the right side, return to rest."""
    rest, start, end = (-0.26, 0.85), (0.45, 1.30), (-0.75, 1.30)
    if t < 0.3:
        a, b, k = rest, start, smoothstep(t / 0.3)
    elif t < 0.55:
        a, b, k = start, end, smoothstep((t - 0.3) / 0.25)
    else:
        a, b, k = end, rest, smoothstep((t - 0.55) / 0.65)
    wx, wy = a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k
    sx, sy = STAND["right_shoulder"]
    return {"right_wrist": (wx, wy, -0.3), "right_elbow": ((sx + wx) / 2, (sy + wy) / 2, -0.15)}


def scratch_arm(t):
    """Right hand up to the side of the head (one hand only, no backswing), hold, back down."""
    up = smoothstep(t / 0.8) * (1 - smoothstep((t - 1.7) / 0.8))
    rest_w, head_w = (-0.26, 0.85), (-0.12, 1.70)
    rest_e, head_e = (-0.24, 1.12), (-0.26, 1.45)
    lerp = lambda a, b: (a[0] + (b[0] - a[0]) * up, a[1] + (b[1] - a[1]) * up)
    w, e = lerp(rest_w, head_w), lerp(rest_e, head_e)
    return {"right_wrist": (w[0], w[1], -0.08), "right_elbow": (e[0], e[1], -0.05)}


def cross_arms(t):
    """Both wrists slowly to the chest (arms crossed)."""
    k = smoothstep(t / 1.0)
    out = {}
    for side, sx in (("left", 1), ("right", -1)):
        wx, wy = STAND[f"{side}_wrist"]
        ex, ey = STAND[f"{side}_elbow"]
        out[f"{side}_wrist"] = (wx + (-0.08 * sx - wx) * k, wy + (1.22 - wy) * k, -0.15 * k)
        out[f"{side}_elbow"] = (ex + (0.22 * sx - ex) * k, ey + (1.12 - ey) * k, -0.05 * k)
    return out


# Unity's BodyHands maps a hand into a "zone" that is the screen: x 0..1 = 1.6 torso lengths either side of the
# shoulders, y 0..1 = from 0.3 torso lengths below the hips to 1.3 above the shoulders. Its point is the wrist pushed
# 30% of the forearm further. With the elbow halfway between shoulder and wrist that is wrist + 15% of (wrist -
# shoulder), so a screen target can be turned back into a wrist position for the standing player.
TORSO_M = STAND["left_shoulder"][1] - STAND["left_hip"][1]
ZONE_BOTTOM_M = STAND["left_hip"][1] - 0.3 * TORSO_M
ZONE_HEIGHT_M = STAND["left_shoulder"][1] + 1.3 * TORSO_M - ZONE_BOTTOM_M
ZONE_WIDTH_M = 2 * 1.6 * TORSO_M


def zone_hand(side, zx, zy):
    """Wrist and elbow (metres) that put this hand's cursor at screen point (zx, zy). The screen is a mirror: the
    player's right hand (negative x) appears on the right."""
    tip = (-(zx - 0.5) * ZONE_WIDTH_M, ZONE_BOTTOM_M + zy * ZONE_HEIGHT_M)
    sx, sy = STAND[f"{side}_shoulder"]
    wx, wy = sx + (tip[0] - sx) / 1.15, sy + (tip[1] - sy) / 1.15
    return {f"{side}_wrist": (wx, wy, -0.30), f"{side}_elbow": ((sx + wx) / 2, (sy + wy) / 2, -0.12)}


def hand_path(side, keys):
    """keys: [(time s, (zx, zy) screen point, or None = arm resting)], linear in between (keys are dense where the
    path curves), eased when leaving or reaching the rest pose. Returns an arm motion."""
    rest = {f"{side}_wrist": (*STAND[f"{side}_wrist"], 0.0), f"{side}_elbow": (*STAND[f"{side}_elbow"], 0.0)}
    poses = [(t, rest if p is None else zone_hand(side, *p)) for t, p in keys]

    def arm(t):
        if t <= poses[0][0]:
            return poses[0][1]
        for (t0, a), (t1, b) in zip(poses, poses[1:]):
            if t <= t1:
                k = (t - t0) / (t1 - t0)
                if a is rest or b is rest:
                    k = smoothstep(k)
                return {j: tuple(a[j][i] + (b[j][i] - a[j][i]) * k for i in range(3)) for j in a}
        return poses[-1][1]
    return arm


def switch_hand(side, spans, ramp=0.3):
    """Pikturo's switch: the free hand lifted to the chest (wrist ~0.8 torso lengths above the hips, out to the side,
    forearm up) during each (on, off) span, resting otherwise; `ramp` seconds to lift or lower."""
    sx = STAND[f"{side}_shoulder"][0]
    out = 1 if sx > 0 else -1                         # the player's left side has positive x
    hip_y = STAND["left_hip"][1]
    up = {f"{side}_wrist": (sx + out * 0.21, hip_y + 0.8 * TORSO_M, -0.25),
          f"{side}_elbow": (sx + out * 0.12, hip_y + 0.25 * TORSO_M, -0.10)}
    rest = {f"{side}_wrist": (*STAND[f"{side}_wrist"], 0.0), f"{side}_elbow": (*STAND[f"{side}_elbow"], 0.0)}

    def arm(t):
        k = 0.0
        for on, off in spans:
            k = max(k, smoothstep((t - on) / ramp) * (1 - smoothstep((t - off) / ramp)))
        return {j: tuple(rest[j][i] + (up[j][i] - rest[j][i]) * k for i in range(3)) for j in rest}
    return arm


def both(*arms):
    return lambda t: {k: v for arm in arms for k, v in arm(t).items()}


def circle_keys(start, center, rx, ry, seconds, steps=32):
    return [(start + seconds * i / steps, (center[0] + rx * math.cos(2 * math.pi * i / steps),
                                          center[1] + ry * math.sin(2 * math.pi * i / steps))) for i in range(steps + 1)]


def arm_keys(side, keys):
    """keys: [(time s, wrist (x, y, z) or None = resting, elbow (x, y, z) or None = halfway to the shoulder)], min-jerk
    between keys (a real arm). Metres, x > 0 = the player's left (the screen's left), z < 0 = toward the camera."""
    sx, sy = STAND[f"{side}_shoulder"]
    rest_w, rest_e = (*STAND[f"{side}_wrist"], 0.0), (*STAND[f"{side}_elbow"], 0.0)

    def pose(w, e):
        if w is None:
            return rest_w, rest_e
        return w, e if e is not None else ((sx + w[0]) / 2, (sy + w[1]) / 2 - 0.04, w[2] * 0.5)
    poses = [(t, *pose(w, e)) for t, w, e in keys]

    def arm(t):
        if t <= poses[0][0]:
            w, e = poses[0][1:]
        else:
            w, e = poses[-1][1:]
            for (t0, w0, e0), (t1, w1, e1) in zip(poses, poses[1:]):
                if t <= t1:
                    k = minjerk((t - t0) / (t1 - t0))
                    w = tuple(a + (b - a) * k for a, b in zip(w0, w1))
                    e = tuple(a + (b - a) * k for a, b in zip(e0, e1))
                    break
        return {f"{side}_wrist": w, f"{side}_elbow": e}
    return arm


def sword_cut(start, end, wind=0.35, swing=0.2, hold=0.2):
    """A sword cut with the right hand: bring it to the start, a fast swing to the end, back to rest."""
    return arm_keys("right", [(0, None, None), (wind, start, None), (wind + 0.12, start, None),
                              (wind + 0.12 + swing, end, None), (wind + 0.32 + swing + hold, end, None), (1.3, None, None)])


def punch(side, to_wrist, to_elbow, extend=0.15):
    """A punch: the fist comes up to the chin (guard), shoots out to `to_wrist` in `extend` seconds, comes back to the
    guard, then the arm drops to rest. Metres, x > 0 = the player's left, z < 0 = toward the camera."""
    sx = STAND[f"{side}_shoulder"][0]
    inward = -1 if sx > 0 else 1                       # toward the body's middle
    guard_w = (sx + inward * 0.05, 1.36, -0.25)        # fist by the chin, elbow down
    guard_e = (sx - inward * 0.05, 1.15, -0.10)
    return arm_keys(side, [(0, None, None), (0.25, guard_w, guard_e), (0.4, guard_w, guard_e),
                           (0.4 + extend, to_wrist, to_elbow), (0.55 + extend, to_wrist, to_elbow),
                           (0.8 + extend, guard_w, guard_e), (1.05, None, None)])


def guard_pose(side):
    """A fist by the chin, the elbow down (metres)."""
    sx = STAND[f"{side}_shoulder"][0]
    inward = -1 if sx > 0 else 1                       # toward the body's middle
    return (sx + inward * 0.05, 1.36, -0.25), (sx - inward * 0.05, 1.15, -0.10)


def guard_hold(side):
    w, e = guard_pose(side)
    return arm_keys(side, [(0, w, e)])


def guard_raise(side, seconds=0.6):
    w, e = guard_pose(side)
    return arm_keys(side, [(0, None, None), (seconds, w, e)])


def guard_punch(side, to_wrist, to_elbow, extend=0.15, fold=False):
    """A punch from the guard and back to it; the other fist stays up (a boxer's flurry, no arm dropping). With
    `fold` (a hook) the arm comes back the way a real one does: the forearm folds in around the elbow (the fist on an
    arc to the chin), then the elbow comes down. (Moving wrist and elbow each in a straight line would shrink the
    forearm: a straight arm sweeping in, which counts as a punch.)"""
    w, e = guard_pose(side)
    other = "left" if side == "right" else "right"
    if not fold:
        return both(arm_keys(side, [(0, w, e), (0.3, w, e), (0.3 + extend, to_wrist, to_elbow),
                                    (0.45 + extend, to_wrist, to_elbow), (0.75 + extend, w, e)]),
                    guard_hold(other))
    out = arm_keys(side, [(0, w, e), (0.3, w, e), (0.3 + extend, to_wrist, to_elbow), (0.45 + extend, to_wrist, to_elbow)])
    folding, lowering = 0.22, 0.25
    t0 = 0.45 + extend
    forearm_out = [a - b for a, b in zip(to_wrist, to_elbow)]
    forearm_in = [a - b for a, b in zip(w, to_elbow)]

    def arm(t):
        if t <= t0:
            return out(t)
        if t <= t0 + folding:
            k = minjerk((t - t0) / folding)
            f = slerp(forearm_out, forearm_in, k)
            return {f"{side}_wrist": tuple(a + b for a, b in zip(to_elbow, f)), f"{side}_elbow": tuple(to_elbow)}
        k = minjerk(min(1.0, (t - t0 - folding) / lowering))
        return {f"{side}_wrist": tuple(w), f"{side}_elbow": tuple(a + (b - a) * k for a, b in zip(to_elbow, e))}
    return both(arm, guard_hold(other))


def slerp(a, b, k):
    """Turns vector a into b by k (0..1) along the arc between them, its length going from |a| to |b|."""
    la, lb = math.sqrt(sum(x * x for x in a)), math.sqrt(sum(x * x for x in b))
    ua, ub = [x / la for x in a], [x / lb for x in b]
    dot = max(-1.0, min(1.0, sum(x * y for x, y in zip(ua, ub))))
    angle = math.acos(dot)
    if angle < 1e-6:
        return [x + (y - x) * k for x, y in zip(a, b)]
    s0, s1 = math.sin((1 - k) * angle) / math.sin(angle), math.sin(k * angle) / math.sin(angle)
    length = la + (lb - la) * k
    return [(s0 * x + s1 * y) * length for x, y in zip(ua, ub)]


def flurry(seconds=8.0, every=0.3, extend=0.13):
    """Jabs at the camera, right and left taking turns every `every` seconds, from the guard and back (a child punching
    as fast as it can)."""
    def keys(side, first):
        w, e = guard_pose(side)
        sx = STAND[f"{side}_shoulder"][0]
        to_w, to_e = (sx, 1.44, -0.60), (sx * 1.05, 1.43, -0.30)
        k = [(0, w, e)]
        t = first
        while t + 2 * every <= seconds:
            k += [(t, w, e), (t + extend, to_w, to_e), (t + extend + 0.05, to_w, to_e), (t + extend + 0.25, w, e)]
            t += 2 * every
        return arm_keys(side, k)
    return both(keys("right", 0.1), keys("left", 0.1 + every))


def tennis_swing(start, end, wind=1.45, swing=0.22, at=1.6, seg=3.4):
    """A racket swing of the right arm: slowly to `start` (the wind-up, well under the game's swing speed of 3 torso
    lengths a second), held, a fast swing to `end` starting `at` seconds in, held a moment, then slowly back to rest by
    the end of the segment."""
    return arm_keys("right", [(0, None, None), (wind, start, None), (at, start, None), (at + swing, end, None),
                              (at + swing + 0.2, end, None), (seg - 0.05, None, None)])


def toss_serve(seg=3.4):
    """A serve as a player does it: a quick flick of the right hand up at 1.0 s (it tosses the ball), held, then the
    swing down at 1.5 s while the ball is near the top (it serves)."""
    return arm_keys("right", [(0, None, None), (0.85, (-0.3, 1.2, 0.0), None), (1.0, (-0.3, 1.2, 0.0), None),
                              (1.15, (-0.3, 1.75, 0.05), None), (1.5, (-0.3, 1.75, 0.05), None),
                              (1.7, (0.05, 1.1, -0.45), None), (1.9, (0.05, 1.1, -0.45), None), (seg - 0.05, None, None)])


def shield_hold(wrist, elbow, hold=1.2):
    """A shield block: the left hand raises the shield into place, holds it there, lowers it."""
    return arm_keys("left", [(0, None, None), (0.35, wrist, elbow), (0.35 + hold, wrist, elbow), (1.8, None, None)])


ARM_MOTIONS = {"bowl": bowl_arm, "swipe": swipe_arm, "scratch": scratch_arm, "cross": cross_arms,
               # Shpata! (sword in the right hand, shield in the left; the screen is a mirror, so the player's left,
               # x > 0, is the screen's left)
               "cut_right": sword_cut((0.25, 1.45, -0.35), (-0.75, 1.35, -0.35)),
               "cut_left": sword_cut((-0.75, 1.35, -0.35), (0.25, 1.45, -0.35)),
               "cut_down": sword_cut((-0.2, 2.0, -0.15), (-0.15, 1.0, -0.5)),
               "cut_up": sword_cut((-0.15, 1.0, -0.5), (-0.2, 2.0, -0.15)),
               "shield_over": shield_hold((0.0, 1.95, -0.3), (0.22, 1.75, -0.2)),
               # Fluturo! (arm length 0.6 m: a straight arm is shoulder + 0.6 m)
               "jab_right": punch("right", (-0.17, 1.44, -0.60), (-0.18, 1.43, -0.30)),
               "jab_left": punch("left", (0.17, 1.44, -0.60), (0.18, 1.43, -0.30)),
               "hook_right": punch("right", (-0.70, 1.48, -0.30), (-0.45, 1.45, -0.15), extend=0.18),
               "guard": both(guard_hold("left"), guard_hold("right")),
               "flurry": flurry(),
               "raise_guard": both(guard_raise("left"), guard_raise("right")),
               "guard_jab_right": guard_punch("right", (-0.17, 1.44, -0.60), (-0.18, 1.43, -0.30)),
               "guard_jab_left": guard_punch("left", (0.17, 1.44, -0.60), (0.18, 1.43, -0.30)),
               "guard_hook_right": guard_punch("right", (-0.70, 1.48, -0.30), (-0.45, 1.45, -0.15), extend=0.18, fold=True),
               # Tenis! (metres; x > 0 = the player's left, z < 0 = toward the camera). The racket is the hand: it
               # meets a ball on the right as the hand passes ~0.25 m right of the shoulders' middle (on the left: left
               # of it), at about the hand's height; the practice feeds arrive ~1.1 m up
               "tennis_forehand": tennis_swing((-0.7, 1.12, 0.15), (0.3, 1.32, -0.4)),
               "tennis_backhand": tennis_swing((0.6, 1.15, 0.1), (-0.7, 1.3, -0.35)),
               "tennis_serve": tennis_swing((-0.3, 1.6, 0.1), (0.05, 1.1, -0.45), swing=0.2),
               # the lob: across and up at ~43 degrees (steeper than 50 is a swing straight up: a lob too)
               "tennis_lob": tennis_swing((-0.6, 0.9, 0.1), (0.25, 1.7, -0.35), swing=0.25),
               "tennis_slice": tennis_swing((-0.55, 1.6, 0.05), (0.15, 0.95, -0.45)),
               # small flicks of the forearm around a still elbow (a third of a torso, 0.22 s: gentle swings, like
               # a Wii remote's): forehand across toward the body, backhand back out, a serve of a flick up and one
               # down, a flick straight up (nothing)
               "flick_forehand": tennis_swing((-0.42, 1.0, -0.12), (-0.27, 1.03, -0.22), swing=0.22),
               "flick_backhand": tennis_swing((-0.12, 1.02, -0.22), (-0.28, 1.0, -0.12), swing=0.22),
               "flick_serve": arm_keys("right", [(0, None, None), (0.85, (-0.3, 1.15, -0.1), None),
                                                  (1.0, (-0.3, 1.15, -0.1), None), (1.2, (-0.3, 1.32, -0.1), None),
                                                  (1.6, (-0.3, 1.32, -0.1), None), (1.82, (-0.27, 1.15, -0.2), None),
                                                  (2.1, (-0.27, 1.15, -0.2), None), (3.35, None, None)]),
               "flick_up": tennis_swing((-0.3, 0.95, -0.15), (-0.3, 1.08, -0.18), swing=0.22),
               "tennis_up": tennis_swing((-0.3, 0.9, 0.0), (-0.25, 1.85, -0.2)),
               "tennis_toss_serve": toss_serve(),
               "shield_left": shield_hold((0.45, 1.5, -0.35), (0.36, 1.2, -0.25)),
               "shield_right": shield_hold((-0.32, 1.48, -0.38), (0.02, 1.28, -0.3)),
               # hold on the colour column (right edge), long enough for a dwell selection, then back down
               "paint_pick": hand_path("right", [(0, None), (0.7, (0.955, 0.45)), (2.0, (0.955, 0.45)), (2.6, None)]),
               # the right hand goes to the start, the left hand switches on, one slow circle, switch off, lower
               "paint_circle": both(hand_path("right", [(0, None), (0.7, (0.78, 0.55)), (1.1, (0.78, 0.55)),
                                                        *circle_keys(1.1, (0.62, 0.55), 0.16, 0.2, 2.4)[1:],
                                                        (3.9, (0.78, 0.55)), (4.6, None)]),
                                    switch_hand("left", [(0.9, 3.6)])),
               # with the switch down the right hand crosses to a new spot (no line!), switch on, a line down, off
               "paint_line": both(hand_path("right", [(0, None), (0.8, (0.3, 0.75)), (1.2, (0.3, 0.75)),
                                                      (2.4, (0.3, 0.35)), (2.7, (0.3, 0.35)), (3.3, None)]),
                                  switch_hand("left", [(1.0, 2.45)]))}

# Vishu!'s wardrobe (DressGame.Layout): the picture is framed so a torso length is the same on screen for everybody,
# and the bubbles sit at fixed offsets from the shoulder centre (torso lengths, x = the screen's right, y = up):
# categories at x -1.36, items at x +1.36, from y 0.86 down in steps of 0.303 (item 0 = nothing); the top row at
# y 1.14, x = (k - 2) * 0.42. The cursor is the wrist pushed 30% of the forearm further (BodyHands, Pikturo).
DRESS_COL, DRESS_TOP, DRESS_STEP = 1.36, 0.86, 0.303


def dress_hand(side, dx, dy):
    """Wrist and elbow (metres) that put this hand's cursor at (dx, dy) torso lengths from the shoulder centre on the
    mirrored screen (the screen's right = the player's right = negative x here)."""
    sy = STAND["left_shoulder"][1]
    tip = (-dx * TORSO_M, sy + dy * TORSO_M)
    shx, shy = STAND[f"{side}_shoulder"]
    wx, wy = shx + (tip[0] - shx) / 1.15, shy + (tip[1] - shy) / 1.15
    return {f"{side}_wrist": (wx, wy, -0.25), f"{side}_elbow": ((shx + wx) / 2, (shy + wy) / 2 - 0.03, -0.1)}


def dress_path(side, keys):
    """keys: [(time s, (dx, dy) or None = resting)], min-jerk between them."""
    rest = {f"{side}_wrist": (*STAND[f"{side}_wrist"], 0.0), f"{side}_elbow": (*STAND[f"{side}_elbow"], 0.0)}
    poses = [(t, rest if p is None else dress_hand(side, *p)) for t, p in keys]

    def arm(t):
        if t <= poses[0][0]:
            return poses[0][1]
        for (t0, a), (t1, b) in zip(poses, poses[1:]):
            if t <= t1:
                k = minjerk((t - t0) / (t1 - t0))
                return {j: tuple(a[j][i] + (b[j][i] - a[j][i]) * k for i in range(3)) for j in a}
        return poses[-1][1]
    return arm


def dress_pick(category, item, again=False):
    """The left hand holds on a category, then the right hand on one of its items (twice with again=True)."""
    cat = (-DRESS_COL, DRESS_TOP - DRESS_STEP * category)
    itm = (DRESS_COL, DRESS_TOP - DRESS_STEP * (item + 1))
    left = dress_path("left", [(0, None), (0.5, cat), (1.4, cat), (1.8, None)])
    if not again:
        right = dress_path("right", [(0, None), (1.3, None), (1.9, itm), (2.9, itm), (3.3, None)])
    else:   # off the bubble and back on: pressed a second time
        right = dress_path("right", [(0, None), (1.3, None), (1.9, itm), (2.8, itm), (3.2, (itm[0] - 0.45, itm[1])),
                                     (3.6, itm), (4.6, itm), (5.0, None)])
    return both(left, right)


def pose_arms(lw, le, rw, re):
    """Both arms to a pose and held (wrist / elbow positions in metres, the player's left = positive x)."""
    return both(arm_keys("left", [(0, None, None), (0.5, lw, le)]), arm_keys("right", [(0, None, None), (0.5, rw, re)]))


ARM_MOTIONS.update({
    "dress_skirt": dress_pick(1, 0), "dress_top": dress_pick(0, 0), "dress_hair": dress_pick(2, 0),
    "dress_head": dress_pick(3, 0), "dress_wings": dress_pick(5, 0, again=True),
    "dress_magic": dress_path("left", [(0, None), (0.5, (-0.84, 1.14)), (1.6, (-0.84, 1.14)), (2.0, None)]),
    # arms up and out (a star)
    "dress_star": pose_arms((0.62, 1.95, -0.1), (0.42, 1.68, -0.05), (-0.62, 1.95, -0.1), (-0.42, 1.68, -0.05)),
    "pose_star": pose_arms((0.62, 1.95, -0.1), (0.42, 1.68, -0.05), (-0.62, 1.95, -0.1), (-0.42, 1.68, -0.05)),
    # hands on the hips, elbows out
    "pose_hips": pose_arms((0.13, 0.97, -0.05), (0.36, 1.16, 0.0), (-0.13, 0.97, -0.05), (-0.36, 1.16, 0.0)),
    # hands together above the head, elbows out
    "pose_heart": pose_arms((0.03, 1.90, -0.05), (0.32, 1.72, 0.0), (-0.03, 1.90, -0.05), (-0.32, 1.72, 0.0)),
})

def repeat_keys(side, poses, every):
    """One arm going through `poses` (wrist, elbow) in turn, `every` seconds from one to the next, min-jerk, looping."""
    def arm(t):
        k = int(t // every)
        a, b = poses[k % len(poses)], poses[(k + 1) % len(poses)]
        f = minjerk((t % every) / every)
        w = tuple(x + (y - x) * f for x, y in zip(a[0], b[0]))
        e = tuple(x + (y - x) * f for x, y in zip(a[1], b[1]))
        return {f"{side}_wrist": w, f"{side}_elbow": e}
    return arm


ARM_MOTIONS.update({
    "arms_out": pose_arms((0.76, 1.42, -0.02), (0.48, 1.42, 0.0), (-0.76, 1.42, -0.02), (-0.48, 1.42, 0.0)),
})


def run_spot(freq=3.4, lift=0.24):
    """Running on the spot: the knees take turns coming up (`freq` steps a second: each leg once every two steps), the
    feet with them, the arms pumping the other way (metres, x > 0 = the player's left)."""
    def m(t):
        out = {}
        for side, phase in (("left", 0.0), ("right", math.pi)):
            up = max(0.0, math.sin(math.pi * freq * t + phase))
            kx, ky = STAND[f"{side}_knee"]
            ax, ay = STAND[f"{side}_ankle"]
            out[f"{side}_knee"] = (kx, ky + lift * up, -0.3 * up)
            out[f"{side}_ankle"] = (ax, ay + lift * 0.9 * up, -0.1 * up)
            # the arm on the other side swings forward and up with this knee
            other = "right" if side == "left" else "left"
            sx, sy = STAND[f"{other}_shoulder"]
            out[f"{other}_elbow"] = (sx * 1.15, sy - 0.25 + 0.08 * up, -0.1 - 0.15 * up)
            out[f"{other}_wrist"] = (sx * 0.9, sy - 0.4 + 0.3 * up, -0.25 - 0.1 * up)
        return out
    return m


def kick_loop(every=0.7):
    """A right-foot kick every `every` seconds: the foot back, then a fast swing up and forward, back down."""
    ax, ay = STAND["right_ankle"]
    kx, ky = STAND["right_knee"]

    def m(t):
        u = (t % every) / every
        if u < 0.25:
            k = -0.4 * smoothstep(u / 0.25)            # the foot goes back a little
        elif u < 0.45:
            k = -0.4 + 1.4 * smoothstep((u - 0.25) / 0.2)   # the kick: fast, up and forward
        else:
            k = 1.0 - smoothstep((u - 0.45) / 0.55)
        up = max(0.0, k)
        return {"right_ankle": (ax + 0.05 * up, ay + 0.42 * up, -0.45 * up + 0.2 * min(0.0, k)),
                "right_knee": (kx, ky + 0.18 * up, -0.25 * up)}
    return m


ARM_MOTIONS.update({
    "run_spot": run_spot(),
    "kick_loop": kick_loop(),
})


# Volejboll!'s moves (metres; x > 0 = the player's left, z < 0 = toward the camera). The bump: both wrists together low
# in front of the belly (a torso below the shoulders, 10 cm apart); the set: both hands up above the head; the spike: the
# right arm raised (slowly: a wind-up, never a hit) and swung down and forward (its fast part 0.2 s)
BUMP_W, BUMP_E = (0.05, 0.95, -0.35), (0.17, 1.17, -0.18)
SET_W, SET_E = (0.16, 1.92, -0.08), (0.24, 1.66, -0.04)
SPIKE_UP_W, SPIKE_UP_E = (-0.24, 1.95, -0.05), (-0.25, 1.68, -0.02)
SPIKE_DOWN_W = (-0.12, 1.02, -0.45)


def mirror(p):
    return (-p[0], p[1], p[2])


def volley_cycle(hits=(4.4,), bump=(0.2, 1.9), set_=(2.3, 3.4), seg=10.0, block=None):
    """One cycle: both hands to the bump and held, to the set and held, then the right arm's spike(s) at `hits`, then
    (`block`) both hands up again: the block."""
    def both_arms(side):
        w = lambda p: p if side == "left" else mirror(p)
        keys = [(0, None, None), (bump[0] - 0.15, None, None), (bump[0], w(BUMP_W), w(BUMP_E)), (bump[1], w(BUMP_W), w(BUMP_E)),
                (bump[1] + 0.25, None, None), (set_[0] - 0.2, None, None), (set_[0], w(SET_W), w(SET_E)), (set_[1], w(SET_W), w(SET_E)),
                (set_[1] + 0.25, None, None)]
        if block:
            keys += [(block[0] - 0.2, None, None), (block[0], w(SET_W), w(SET_E)), (block[1], w(SET_W), w(SET_E)), (block[1] + 0.25, None, None)]
        if side == "right":
            for at in hits:
                keys += [(at - 0.75, None, None), (at - 0.2, SPIKE_UP_W, SPIKE_UP_E), (at, SPIKE_UP_W, SPIKE_UP_E),
                         (at + 0.2, SPIKE_DOWN_W, None), (at + 0.35, SPIKE_DOWN_W, None),
                         (min(at + 1.25, block[0] - 0.3) if block else at + 1.25, None, None)]
        keys.append((seg - 0.05, None, None))
        keys.sort(key=lambda k: k[0])
        return arm_keys(side, keys)
    return both(both_arms("left"), both_arms("right"))


ARM_MOTIONS.update({
    "volley_cycle": volley_cycle(block=(7.6, 8.9)),
    "volley_serve_cycle": volley_cycle(hits=(4.4, 6.4), block=(7.6, 8.9)),
    "volley_duo_cycle": volley_cycle(hits=(4.6,), bump=(0.2, 2.2), set_=(2.6, 3.9), seg=6.0),
    # the same, half a cycle later (it starts and ends at rest, so the wrap is smooth)
    "volley_duo_cycle_late": (lambda m: (lambda t: m((t + 3.0) % 6.0)))(volley_cycle(hits=(4.6,), bump=(0.2, 2.2), set_=(2.6, 3.9), seg=6.0)),
})


# Gara!: an invisible steering wheel held in front of the chest (toward the camera), turned by the hands: the player's left
# hand (x > 0) rises turning right
WHEEL_C, WHEEL_R = (0.0, 1.16, -0.32), 0.2


def wheel_pose(deg, up=0.0):
    a = math.radians(deg)
    cx, cy, cz = WHEEL_C
    cy += up
    lw = (WHEEL_R * math.cos(a), cy + WHEEL_R * math.sin(a), cz)
    rw = (-WHEEL_R * math.cos(a), cy - WHEEL_R * math.sin(a), cz)
    le = ((0.19 + lw[0]) / 2 + 0.07, (1.42 + lw[1]) / 2 - 0.08, cz * 0.45)
    re = ((-0.19 + rw[0]) / 2 - 0.07, (1.42 + rw[1]) / 2 - 0.08, cz * 0.45)
    return {"left_wrist": lw, "left_elbow": le, "right_wrist": rw, "right_elbow": re}


UP_POSE = {"left_wrist": (0.18, 1.95, -0.05), "left_elbow": (0.22, 1.66, 0.0),
           "right_wrist": (-0.18, 1.95, -0.05), "right_elbow": (-0.22, 1.66, 0.0)}


def kart_drive(seg=8.0, amp=32.0, hard=(4.4, 5.9), up=(6.5, 7.3)):
    """One cycle of driving: the wheel turned left and right (a sine, 4 s), a hard right turn held (a drift), both hands
    up and back (the item)."""
    def arm(t):
        t = t % seg
        deg = amp * math.sin(2 * math.pi * t / 4.0)
        if hard[0] - 0.3 <= t <= hard[1] + 0.3:
            k = smoothstep((t - (hard[0] - 0.3)) / 0.3) * (1 - smoothstep((t - hard[1]) / 0.3))
            deg = deg + (62.0 - deg) * k
        pose = wheel_pose(deg)
        if up[0] - 0.25 <= t <= up[1] + 0.25:
            k = smoothstep((t - (up[0] - 0.25)) / 0.25) * (1 - smoothstep((t - up[1]) / 0.25))
            pose = {n: tuple(a + (b - a) * k for a, b in zip(pose[n], UP_POSE[n])) for n in pose}
        return pose
    return arm


ARM_MOTIONS.update({
    "kart_level": lambda t: wheel_pose(0.0),
    "kart_drive": kart_drive(),
    # the same, half a cycle later (it starts and ends with the wheel level, so the wrap is smooth)
    "kart_drive_late": (lambda m: (lambda t: m((t + 4.0) % 8.0)))(kart_drive()),
})


# Whole-body offsets (dx, dy) in metres over time.
BODY_MOTIONS = {
    "sway": lambda t: (0.06 * math.sin(2 * math.pi * 0.5 * t), 0.0),
    "tiptoe": lambda t: (0.0, 0.04 * smoothstep(t / 0.6) * (1 - smoothstep((t - 1.2) / 0.6))),
}


def body_at(state, jump_y, arm=None):
    joints = {}
    for name, (x, y) in STAND.items():
        if name in HANDS_UP:
            hx, hy = HANDS_UP[name]
            x += (hx - x) * state["hands"]
            y += (hy - y) * state["hands"]
        c = state["crouch"]
        z = 0.0
        if arm and name in arm:
            x, y, z = arm[name]   # standing coordinates: the arm crouches with the body below
        if name in UPPER_BODY:
            y -= 0.42 * c
        elif name.endswith("hip"):
            y -= 0.40 * c
        elif name.endswith("knee"):
            y -= 0.15 * c
            x += math.copysign(0.08 * c, x)
        joints[name] = (x + state["x"], y + jump_y, z)
    return joints


FACE_DEPTH = {"nose": -0.09, "left_eye": -0.07, "right_eye": -0.07}   # metres in front of the head's axis


def turn_body(joints_m, degrees):
    """The whole body turned around the vertical axis through the hips (the face in front of the head, so the nose
    moves across it). + = the chest toward the screen's right: the player's right shoulder (x < 0, the screen's right
    in the mirrored game) goes back."""
    a = math.radians(degrees)
    c, s = math.cos(a), math.sin(a)
    hx = (joints_m["left_hip"][0] + joints_m["right_hip"][0]) / 2
    hz = (joints_m["left_hip"][2] + joints_m["right_hip"][2]) / 2
    out = {}
    for name, (x, y, z) in joints_m.items():
        dx, dz = x - hx, z - hz + FACE_DEPTH.get(name, 0.0)
        out[name] = (hx + dx * c + dz * s, y, hz - dx * s + dz * c)
    return out


CAMERA_DISTANCE_M, CAMERA_HEIGHT_M = 2.8, 1.0


def perspective(x, y, near):
    """A point `near` metres closer to the camera than the plane the picture is drawn for looks bigger: scaled around
    the camera's axis (the picture's middle, at the camera's height)."""
    k = CAMERA_DISTANCE_M / (CAMERA_DISTANCE_M - near)
    return x * k, CAMERA_HEIGHT_M + (y - CAMERA_HEIGHT_M) * k


class Glitches:
    """The box's pose-model glitches (2026-11-06, from its recordings), `rate` a second on average: a wrist flipped to
    another spot 15-50 cm away for one or two frames, or pulled most of the way onto its elbow for one to three, the
    model less sure of it meanwhile. The right hand (the racket's) gets 60 % of them."""

    def __init__(self, rate, fps):
        self.chance = rate / fps
        self.side, self.frames, self.kind, self.amount, self.vis = None, 0, None, None, 1.0

    def __call__(self, joints_m):
        if self.frames <= 0 and random.random() < self.chance:
            self.side = "right" if random.random() < 0.6 else "left"
            if random.random() < 0.5:
                angle = random.uniform(0, 2 * math.pi)
                size = random.uniform(0.15, 0.5)
                self.kind, self.amount, self.frames = "flip", (math.cos(angle) * size, math.sin(angle) * size), random.randint(1, 2)
            else:
                self.kind, self.amount, self.frames = "collapse", random.uniform(0.5, 0.8), random.randint(1, 3)
            self.vis = random.uniform(0.7, 0.85)
        if self.frames <= 0:
            return joints_m, {}
        self.frames -= 1
        joints = dict(joints_m)
        w, e = f"{self.side}_wrist", f"{self.side}_elbow"
        x, y, z = joints[w]
        if self.kind == "flip":
            joints[w] = (x + self.amount[0], y + self.amount[1], z)
        else:
            ex, ey, ez = joints[e]
            joints[w] = (x + (ex - x) * self.amount, y + (ey - y) * self.amount, z + (ez - z) * self.amount)
        return joints, {w: self.vis}


def to_person(joints_m, noise=0.003, world_noise=0.005, near=0.0, track=0, vis=None):
    out, world = [], []
    hx = (joints_m["left_hip"][0] + joints_m["right_hip"][0]) / 2
    hy = (joints_m["left_hip"][1] + joints_m["right_hip"][1]) / 2
    hz = (joints_m["left_hip"][2] + joints_m["right_hip"][2]) / 2
    for name in JOINT_NAMES:
        x, y, z = joints_m[name]
        px, py = perspective(x, y, near) if near else (x, y)
        nx = 0.5 + px / VIEW_WIDTH_M + random.gauss(0, noise)
        ny = 0.95 - py / VIEW_HEIGHT_M + random.gauss(0, noise)
        nz = z / VIEW_WIDTH_M  # MediaPipe-style depth: same scale as x, negative = toward the camera
        out.append((nx, ny, nz, (vis or {}).get(name, 0.98)))
        # MediaPipe world joints: metres, origin at the hips, x image-right, y DOWN, z toward the camera = negative
        world.append((x - hx + random.gauss(0, world_noise), -(y - hy) + random.gauss(0, world_noise),
                      z - hz + random.gauss(0, world_noise)))
    return Person(out, world=world, track=track)


def frame_joints(prev, target, t):
    """One player's joints (metres) `t` seconds into a segment going from state `prev` to `target`, and how near the
    camera they stand."""
    k = smoothstep(t / TRANSITION_S)
    state = {key: prev.get(key, 0.0) + (target.get(key, 0.0) - prev.get(key, 0.0)) * k
             for key in ("x", "crouch", "hands", "near")}
    jump_y = JUMP_HEIGHT_M * math.sin(math.pi * t / JUMP_TIME_S) if target.get("jump") and t < JUMP_TIME_S else 0.0
    arm = ARM_MOTIONS[target["arm"]](t) if target.get("arm") else None
    dx, dy = BODY_MOTIONS[target["move"]](t) if target.get("move") else (0.0, 0.0)
    if target.get("hop"):
        extra_crouch, hop_y = hop(t, *target["hop"])
        state["crouch"] += extra_crouch
        dy += hop_y
    if target.get("hops"):   # hop after hop, one every `hops` seconds
        extra_crouch, hop_y = hop(t % target["hops"], 0.10, 0.36, start=0.02)
        state["crouch"] += extra_crouch
        dy += hop_y
    state["x"] += dx
    joints_m = body_at(state, jump_y + dy, arm)
    if target.get("turn"):
        a0, a1 = target["turn"]
        joints_m = turn_body(joints_m, a0 + (a1 - a0) * smoothstep(t / target.get("turn_s", 1.2)))
    return joints_m, state["near"]


def end_state(target):
    return {key: target.get(key, 0.0) for key in ("x", "crouch", "hands", "near")}


# ---- drawn camera picture for the camera preview stream (what a camera would see: not mirrored, no noise)

VIDEO_W, VIDEO_H = 640, 360
_room = None


def room_background():
    """A simple living room: wall, floor, window, sofa, picture. Drawn once (BGR, like a camera frame in OpenCV)."""
    global _room
    if _room is None:
        import cv2
        import numpy as np
        h, w = VIDEO_H, VIDEO_W
        img = np.zeros((h, w, 3), np.uint8)
        floor_y = int(0.80 * h)
        for y in range(h):
            k = y / h
            img[y] = (150 + 40 * k, 196 + 20 * k, 222 + 15 * k) if y < floor_y else (88, 120, 150)
        cv2.rectangle(img, (40, 50), (170, 190), (235, 225, 200), -1)          # window
        cv2.rectangle(img, (40, 50), (170, 190), (90, 90, 90), 4)
        cv2.line(img, (105, 50), (105, 190), (90, 90, 90), 3)
        cv2.rectangle(img, (440, 200), (620, 290), (160, 80, 70), -1)        # sofa
        cv2.rectangle(img, (440, 175), (620, 215), (140, 70, 60), -1)
        cv2.rectangle(img, (480, 70), (580, 140), (230, 160, 60), -1)        # picture
        cv2.rectangle(img, (480, 70), (580, 140), (40, 40, 40), 3)
        _room = img
    return _room.copy()


def render_camera(joints_m):
    """The player drawn over the room: trousers, shirt, bare forearms, head. Raw camera view (not mirrored)."""
    import cv2
    import numpy as np
    img = room_background()

    def px(name):
        x, y, _ = joints_m[name]
        return int((0.5 + x / VIEW_WIDTH_M) * VIDEO_W), int((0.95 - y / VIEW_HEIGHT_M) * VIDEO_H)

    limb = max(2, int(0.12 / VIEW_HEIGHT_M * VIDEO_H))
    skin, shirt, trousers, hair = (150, 180, 230), (200, 110, 50), (90, 60, 40), (40, 50, 70)
    for side in ("left", "right"):
        cv2.line(img, px(f"{side}_hip"), px(f"{side}_knee"), trousers, limb + 3, cv2.LINE_AA)
        cv2.line(img, px(f"{side}_knee"), px(f"{side}_ankle"), trousers, limb + 1, cv2.LINE_AA)
    torso = [px("left_shoulder"), px("right_shoulder"), px("right_hip"), px("left_hip")]
    cv2.fillConvexPoly(img, np.array(torso, np.int32), shirt, cv2.LINE_AA)
    for side in ("left", "right"):
        cv2.line(img, px(f"{side}_shoulder"), px(f"{side}_elbow"), shirt, limb + 2, cv2.LINE_AA)
        cv2.line(img, px(f"{side}_elbow"), px(f"{side}_wrist"), skin, limb, cv2.LINE_AA)
        cv2.circle(img, px(f"{side}_wrist"), limb // 2 + 2, skin, -1, cv2.LINE_AA)
    nose = px("nose")
    head_r = int(0.11 / VIEW_HEIGHT_M * VIDEO_H)
    cv2.circle(img, (nose[0], nose[1] - head_r // 3), head_r, skin, -1, cv2.LINE_AA)
    cv2.ellipse(img, (nose[0], nose[1] - head_r // 2), (head_r, head_r), 0, 180, 360, hair, -1, cv2.LINE_AA)
    return img


def main():
    p = argparse.ArgumentParser(description="Send a synthetic test player over UDP")
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=5005)
    p.add_argument("--fps", type=float, default=30)
    p.add_argument("--once", action="store_true", help="play the script once instead of looping")
    p.add_argument("--script", choices=sorted(SCRIPTS), default="basic")
    p.add_argument("--alternate", action="store_true",
                   help="two-player scripts: one player per packet, taking turns, with track ids (like the box's NPU "
                        "sharing its landmark runs between two people: each player at half the rate)")
    p.add_argument("--video-control-port", type=int, default=CONTROL_PORT,
                   help="send a drawn camera picture while the game asks for one; 0 = never")
    p.add_argument("--glitches", type=float, default=0.0,
                   help="the box's pose-model glitches this many times a second on average (one player): wrists flipped "
                        "or pulled onto their elbows for a frame or a few")
    args = p.parse_args()
    script = SCRIPTS[args.script]
    glitches = Glitches(args.glitches, args.fps) if args.glitches > 0 else None

    sender = UdpSender(args.host, args.port)
    video = VideoStreamer(args.video_control_port) if args.video_control_port else None
    fps_meter = FpsMeter()
    print(f"Fake player ({args.script}) -> udp://{args.host}:{args.port} at {args.fps:.0f} fps")
    frame = 0
    duo = isinstance(script[0][2], tuple)
    prev = [end_state(t) for t in script[-1][2]] if duo else dict(script[-1][2])
    period = 1.0 / args.fps
    next_tick = time.perf_counter()
    try:
        while True:
            for name, duration, target in script:
                try:
                    print(f"[{time.strftime('%H:%M:%S')}] {name}", flush=True)
                except OSError:
                    pass  # no console (e.g. started by the Unity tests): keep sending
                seg_start = time.perf_counter()
                while (t := time.perf_counter() - seg_start) < duration:
                    if duo:
                        people = []
                        for i, (pv, tg) in enumerate(zip(prev, target)):
                            if tg.get("absent"):
                                continue
                            joints, near = frame_joints(pv, tg, t)
                            joints = {n: (x + DUO_X[i], y, z) for n, (x, y, z) in joints.items()}
                            people.append(to_person(joints, near=near, track=i + 1 if args.alternate else 0))
                        if args.alternate and people:
                            people = [people[frame % len(people)]]
                    else:
                        joints_m, near = frame_joints(prev, target, t)
                        seen, vis = glitches(joints_m) if glitches else (joints_m, None)
                        people = [] if target.get("absent") else [to_person(seen, near=near, vis=vis)]
                    t_ms = now_ms()
                    packet = build_packet("py-fake", frame, t_ms, WIDTH, HEIGHT, fps_meter.tick(), 0.0, people,
                                          camera_fps=30.0, model="fake")
                    sender.send(packet)
                    if video and video.wanted() and not duo:
                        video.send(render_camera(joints_m) if people else room_background(), t_ms, frame)
                    frame += 1
                    next_tick += period
                    time.sleep(max(0.0, next_tick - time.perf_counter()))
                prev = [end_state(tg) for tg in target] if duo else end_state(target)
            if args.once:
                break
    except KeyboardInterrupt:
        pass
    finally:
        sender.close()
        if video:
            video.close()


if __name__ == "__main__":
    assert J["right_ankle"] == 16
    main()
