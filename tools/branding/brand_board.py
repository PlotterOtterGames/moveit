"""Brand board: one self-contained HTML page (logo, icon, banner, boot animation, colours, type) for reviews.

Built from the same sources as everything else (brand.json, logo.py, the generated files), so it never drifts.
Run make_branding.py first, then:
    motion-service-py/.venv/Scripts/python tools/branding/brand_board.py --out brand-board.html
"""

import argparse
import base64
import json
from pathlib import Path

import logo
from make_branding import BRAND_DIR, BRAND_JSON, OUT, UNITY_BRANDING, colors_on_dark, colors_on_light, icon_svg, rgb


def data_uri(path, mime):
    return f"data:{mime};base64," + base64.b64encode(Path(path).read_bytes()).decode("ascii")


def inline_svg(svg_text, cls=""):
    s = svg_text.replace('id="figure"', 'class="figure"')
    s = s.replace("<svg ", f'<svg class="{cls}" role="img" aria-label="Tundu" ', 1)
    # drop fixed width/height: CSS sizes them
    head, rest = s.split(">", 1)
    head = " ".join(p for p in head.split(" ") if not p.startswith(("width=", "height=")))
    return head + ">" + rest


def contrast(a, b):
    def lum(c):
        def ch(v):
            v /= 255
            return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
        r, g, bl = (ch(x) for x in rgb(c))
        return 0.2126 * r + 0.7152 * g + 0.0722 * bl
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


def build(brand):
    els, box = logo.wordmark()
    fig, fig_box = logo.figure_only()
    white = {r: "#FFFFFF" for r in ("letters", "figure", "head")}
    night = {r: brand["background"] for r in ("letters", "figure", "head")}
    swatches = [
        ("Sun", "primary", "Focus, highlights, the figure in the logo"),
        ("Coral", "secondary", "The logo's head, alerts, a second accent"),
        ("Sky", "accent", "Links, info, player 2"),
        ("Grape", "grape", "Decoration and big shapes only (too dark for small text on Night)"),
        ("Leaf", "leaf", "Success, “Bravo!”, player 4"),
        ("Night", "background", "TV screens, the box's whole world"),
        ("Surface", "surface", "Cards and panels on Night"),
        ("Cloud", "text", "Text on Night"),
        ("Mist", "textMuted", "Secondary text on Night"),
    ]
    sw_html = []
    for name, key, role in swatches:
        hexv = brand[key]
        c = contrast(hexv, brand["background"])
        badge = "" if key == "background" else f'<span class="ratio">{c:.1f}:1 on Night</span>'
        sw_html.append(f'<figure class="swatch"><div class="chip" style="background:{hexv}"></div><figcaption>'
                       f'<b>{name}</b><code>{hexv}</code><span>{role}</span>{badge}</figcaption></figure>')
    players = "".join(f'<div class="player"><span style="background:{c}"></span>Lojtari {i + 1}</div>'
                      for i, c in enumerate(brand["players"]))

    icon = inline_svg(icon_svg(brand), "icon-svg")
    page = TEMPLATE
    for k, v in {
        "{{HERO}}": inline_svg(logo.svg(els, box, colors_on_dark(brand), pad=0.25), "hero-logo"),
        "{{ON_LIGHT}}": inline_svg(logo.svg(els, box, colors_on_light(brand), pad=0.25)),
        "{{WHITE}}": inline_svg(logo.svg(els, box, white, pad=0.25)),
        "{{NIGHT}}": inline_svg(logo.svg(els, box, night, pad=0.25)),
        "{{MARK}}": inline_svg(logo.svg(fig, fig_box, {"figure": brand["primary"], "head": brand["secondary"]}, pad=0.3), "mark"),
        "{{ICON_L}}": icon, "{{ICON_M}}": icon, "{{ICON_S}}": icon,
        "{{BANNER}}": data_uri(UNITY_BRANDING / "tv_banner.png", "image/png"),
        "{{BOOT}}": data_uri(OUT / "boot-preview.gif", "image/gif"),
        "{{SWATCHES}}": "\n".join(sw_html),
        "{{PLAYERS}}": players,
        "{{TAG_SQ}}": brand["taglineSq"], "{{TAG_EN}}": brand["taglineEn"],
        "{{PACKAGE}}": brand["androidPackage"],
        "{{BOOT_KB}}": f'{(OUT / "bootanimation.zip").stat().st_size / 1024 / 1024:.1f} MB',
    }.items():
        page = page.replace(k, v)
    for key in ("primary", "secondary", "accent", "grape", "leaf", "background", "surface", "text", "textMuted"):
        page = page.replace("{{" + key + "}}", brand[key])
    return page


TEMPLATE = r"""<title>Tundu Brand Board</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Fredoka:wght@400;500;600;700&family=Nunito:ital,wght@0,400;0,600;0,700;0,800;1,400&display=swap">
<style>
/* Layout: a TV-night canvas, one 1120px column of stacked sections, the hero logo hops like on the box. */
:root {
  color-scheme: dark;
  --night: {{background}}; --surface: {{surface}}; --sun: {{primary}}; --coral: {{secondary}};
  --sky: {{accent}}; --grape: {{grape}}; --leaf: {{leaf}}; --cloud: {{text}}; --mist: {{textMuted}};
  --line: color-mix(in srgb, var(--mist) 22%, transparent);
  --paper: #F4F5FB;
  --display: "Fredoka", "Baloo 2", "Arial Rounded MT Bold", system-ui, sans-serif;
  --body: "Nunito", "Segoe UI", system-ui, sans-serif;
  --mono: ui-monospace, "Cascadia Mono", Consolas, monospace;
}
* { box-sizing: border-box }
body { margin: 0; background: var(--night); color: var(--cloud); font: 400 17px/1.6 var(--body) }
.wrap { max-width: 1120px; margin: 0 auto; padding: 0 20px 80px }
section { padding-block: 56px 8px; border-top: 1px solid var(--line) }
h1, h2, h3 { font-family: var(--display); font-weight: 600; text-wrap: balance; margin: 0 }
h2 { font-size: clamp(28px, 4vw, 40px); line-height: 1.1; margin-bottom: 8px }
h3 { font-size: 20px; font-weight: 500; color: var(--mist) }
.lead { color: var(--mist); max-width: 64ch; margin: 0 0 28px }
.eyebrow { font: 700 13px/1 var(--body); letter-spacing: .14em; text-transform: uppercase; color: var(--sun) }
code { font-family: var(--mono); font-size: .92em }
a { color: var(--sky) }
:focus-visible { outline: 3px solid var(--sun); outline-offset: 3px }

/* hero */
.hero { padding-block: 48px 40px; display: grid; gap: 28px }
.hero-logo { width: min(100%, 760px); height: auto; display: block; overflow: visible }
.hero .figure { transform-box: fill-box; transform-origin: 50% 100%; animation: hop 1.6s infinite }
@keyframes hop {
  0%   { transform: translateY(0) scale(1.07, .88); animation-timing-function: cubic-bezier(.2,.8,.3,1) }
  10%  { transform: translateY(0) scale(1, 1) }
  45%  { transform: translateY(-16%) scale(.97, 1.04); animation-timing-function: cubic-bezier(.6,0,.9,.4) }
  78%  { transform: translateY(0) scale(1, 1) }
  86%  { transform: translateY(0) scale(1.07, .88) }
  100% { transform: translateY(0) scale(1.07, .88) }
}
@media (prefers-reduced-motion: reduce) { .hero .figure { animation: none } }
.meaning { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 20px 40px; max-width: 900px }
.meaning p { margin: 0 }
.meaning .big { font: 600 30px/1.2 var(--display) }
.meaning .big em { font-style: normal; color: var(--sun) }
.say { color: var(--mist) }

/* checks */
.checks { width: 100%; border-collapse: collapse; font-size: 16px }
.checks th, .checks td { text-align: left; padding: 12px 14px 12px 0; border-bottom: 1px solid var(--line); vertical-align: top }
.checks th { font: 700 13px var(--body); letter-spacing: .08em; text-transform: uppercase; color: var(--mist) }
.pill { display: inline-block; font: 700 13px/1 var(--body); padding: 6px 10px; border-radius: 99px; white-space: nowrap }
.ok { background: color-mix(in srgb, var(--leaf) 22%, transparent); color: var(--leaf) }
.no { background: color-mix(in srgb, var(--coral) 22%, transparent); color: #FF8A80 }
.todo { background: color-mix(in srgb, var(--sun) 18%, transparent); color: var(--sun) }
.table-scroll { overflow-x: auto }
.note { color: var(--mist); font-size: 15px; max-width: 70ch }

/* logo grid */
.logos { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px }
.tile { border-radius: 20px; padding: 32px 28px; display: grid; gap: 14px; align-content: space-between; min-height: 200px }
.tile svg { width: 100%; height: auto; max-height: 120px }
.tile small { font: 600 14px var(--body) }
.on-night { background: var(--surface) } .on-night small { color: var(--mist) }
.on-paper { background: var(--paper); color: var(--night) } .on-paper small { color: #4A5180 }
.on-sun { background: var(--sun); color: var(--night) } .on-sun small { color: #5b4300 }
.on-coral { background: var(--coral); color: var(--night) } .on-coral small { color: #4d0f0a }
.anatomy { display: grid; grid-template-columns: minmax(0, 220px) minmax(0, 1fr); gap: 28px; align-items: center; margin-top: 28px }
.anatomy .mark { width: 100%; height: auto; max-width: 220px }
.anatomy ul { margin: 0; padding-left: 20px; display: grid; gap: 8px }
.rules { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 12px 32px; margin-top: 28px }
.rules p { margin: 0 }
.rules b { font-family: var(--display); font-weight: 600; display: block; color: var(--sun) }

/* icon, banner, boot */
.app { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 32px; align-items: start }
.icons { display: flex; gap: 22px; align-items: end; flex-wrap: wrap }
.icon-svg { display: block; width: 100%; height: auto }
.i-l { width: 168px } .i-m { width: 96px } .i-s { width: 48px }
.icons figure { margin: 0; display: grid; gap: 8px; justify-items: center; color: var(--mist); font-size: 13px }
.banner { width: 100%; max-width: 480px; border-radius: 10px; display: block; image-rendering: auto }
.tv { background: #05060c; padding: 14px 14px 30px; border-radius: 18px; position: relative; max-width: 100% }
.tv::after { content: ""; position: absolute; left: 50%; bottom: 10px; width: 64px; height: 6px; margin-left: -32px; border-radius: 3px; background: #1d2030 }
.tv img { width: 100%; display: block; border-radius: 6px }
figcaption.cap { color: var(--mist); font-size: 15px; margin-top: 12px; max-width: 60ch }

/* colour */
.swatches { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 16px }
.swatch { margin: 0; background: var(--surface); border-radius: 16px; overflow: hidden }
.chip { height: 96px; border-bottom: 1px solid var(--line) }
.swatch figcaption { padding: 12px 14px 14px; display: grid; gap: 2px; font-size: 14px }
.swatch b { font: 600 18px var(--display) }
.swatch code { color: var(--cloud) }
.swatch span { color: var(--mist) }
.swatch .ratio { font-variant-numeric: tabular-nums; color: var(--cloud); opacity: .8 }
.players { display: flex; flex-wrap: wrap; gap: 10px 22px; margin-top: 20px }
.player { display: flex; align-items: center; gap: 10px; font-weight: 700 }
.player span { width: 28px; height: 28px; border-radius: 50% 50% 50% 8px; display: block }

/* type */
.type { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 24px }
.specimen { background: var(--surface); border-radius: 20px; padding: 28px; min-width: 0 }
.specimen .name { font: 500 15px var(--body); color: var(--mist); margin-bottom: 12px }
.specimen .d { font: 600 clamp(40px, 6vw, 64px)/1.05 var(--display); overflow-wrap: anywhere }
.specimen .b { font: 400 20px/1.5 var(--body) }
.specimen .glyphs { font: 500 26px/1.4 var(--display); color: var(--sun); overflow-wrap: anywhere; margin-top: 12px }
.scale { width: 100%; border-collapse: collapse; margin-top: 24px; font-variant-numeric: tabular-nums }
.scale td { padding: 10px 14px 10px 0; border-bottom: 1px solid var(--line); vertical-align: baseline }
.scale td:first-child { color: var(--mist); width: 120px; font-size: 14px }
.scale td:nth-child(2) { color: var(--mist); width: 110px; font-size: 14px; white-space: nowrap }

/* sample screen */
.screen { background: var(--night); border: 1px solid var(--line); border-radius: 22px; padding: 28px; overflow: hidden }
.screen .top { display: flex; justify-content: space-between; align-items: center; gap: 16px; flex-wrap: wrap; margin-bottom: 22px }
.screen .top svg { width: 140px; height: auto }
.screen .hint { font: 700 15px var(--body); color: var(--mist) }
.cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 18px }
.card { background: var(--surface); border-radius: 22px; padding: 18px; display: grid; gap: 10px; border: 4px solid transparent }
.card .art { height: 92px; border-radius: 14px; display: grid; place-items: center; font: 700 34px var(--display); color: var(--night) }
.card b { font: 600 24px/1 var(--display) }
.card span { color: var(--mist); font-size: 14px }
.card.focus { border-color: var(--sun); transform: scale(1.04); box-shadow: 0 10px 30px rgba(0,0,0,.35) }
.play { justify-self: start; background: var(--sun); color: var(--night); font: 700 16px var(--body); padding: 8px 16px; border-radius: 99px }

.files { columns: 2 300px; column-gap: 40px; padding-left: 18px; margin: 0 }
.files li { break-inside: avoid; margin-bottom: 8px }
.files code { color: var(--sun) }
@media (max-width: 560px) {
  .anatomy { grid-template-columns: 1fr }
  .i-l { width: 128px }
}
</style>

<div class="wrap">
  <header class="hero">
    <span class="eyebrow">Brand board &middot; draft for review &middot; October 2026</span>
    {{HERO}}
    <div class="meaning">
      <p class="big"><em>Tundu!</em> Move! Shake it!</p>
      <p>The imperative of <i>tund</i>, &ldquo;to shake, to move&rdquo;: exactly what the box asks you to do. Families can say it as a verb too: <i>&ldquo;Tunduni!&rdquo;</i> Two syllables, no &euml; or &ccedil;, easy to shout across a living room.</p>
      <p><span class="eyebrow">Tagline</span><br><b>{{TAG_SQ}}</b><br><span class="say">{{TAG_EN}}</span></p>
    </div>
  </header>

  <section id="name">
    <span class="eyebrow">Name check, 7 October 2026</span>
    <h2>Tundu is free in our categories</h2>
    <p class="lead">Checked for software and devices (Nice class 9), games and toys (28) and entertainment (41), the classes a motion-game box needs.</p>
    <div class="table-scroll">
    <table class="checks">
      <thead><tr><th>Where</th><th>Result</th><th>Details</th></tr></thead>
      <tbody>
        <tr><td>Trademarks (TMview: EUIPO, WIPO, Albania, North Macedonia and 70+ offices)</td><td><span class="pill ok">Clear</span></td><td>Only two food marks named TUNDU (Turkey 2021, Italy 2018, classes 29&ndash;31). Nothing in 9, 28 or 41.</td></tr>
        <tr><td><code>tundu.al</code>, <code>tundu.com.al</code></td><td><span class="pill ok">Free</span></td><td>No nameservers. Register <code>tundu.al</code> first: it's the home address for Albanian families.</td></tr>
        <tr><td><code>tundu.games</code>, <code>tundu.app</code></td><td><span class="pill ok">Free</span></td><td>No nameservers.</td></tr>
        <tr><td><code>tundu.com</code></td><td><span class="pill no">Taken</span></td><td>Registered, no website answering. Not needed for a local launch.</td></tr>
        <tr><td>Google Play</td><td><span class="pill ok">Clear</span></td><td>No app called Tundu (closest: &ldquo;Tunduk&rdquo;, a Kyrgyz government app).</td></tr>
        <tr><td>Kosovo IP agency, Apple App Store, social handles</td><td><span class="pill todo">Not checked</span></td><td>Kosovo isn't in TMview. Check before the Kosovo launch.</td></tr>
      </tbody>
    </table>
    </div>
    <p class="note">&ldquo;Free&rdquo; means no DNS nameservers on 7 October 2026; a registrar confirms it. Before printing boxes, register the domains and file the mark in Albania (DPPI) for classes 9, 28 and 41. The Android app id becomes <code>{{PACKAGE}}</code>.</p>
  </section>

  <section id="logo">
    <span class="eyebrow">Logo</span>
    <h2>One rounded stroke, and the last letter jumps</h2>
    <p class="lead">Our own lettering, not a font. The final <b>u</b> is a kid cheering mid-jump: its stems are the arms, the bowl is the body, the coral dot is the head. Arms up is also the gesture that starts a game on the box.</p>
    <div class="logos">
      <div class="tile on-night">{{WHITE_ON_NIGHT}}<small>Main logo, on Night</small></div>
      <div class="tile on-paper">{{ON_LIGHT}}<small>On light backgrounds (box, print, web)</small></div>
      <div class="tile on-coral">{{NIGHT}}<small>One colour, Night</small></div>
      <div class="tile on-night">{{WHITE}}<small>One colour, white (photos, embossing)</small></div>
    </div>
    <div class="anatomy">
      {{MARK}}
      <ul>
        <li><b>The mark</b> is the figure alone: app icon, favicon, stickers, the corner of the screen during a game.</li>
        <li><b>Stroke</b> S is the unit: every letter is one round-capped stroke of width S. The head is 1.56&thinsp;S across.</li>
        <li><b>The jump:</b> the figure floats S/2 above the baseline. In animation it hops; in print it stays still.</li>
      </ul>
    </div>
    <div class="rules">
      <p><b>Clear space</b>At least 1&nbsp;S around the logo on every side.</p>
      <p><b>Smallest size</b>The wordmark at 28&nbsp;px tall on a TV; below that, use the mark.</p>
      <p><b>Keep</b>The head separate from the arms, the colours from the palette, the figure upright.</p>
      <p><b>Avoid</b>Outlines, shadows, stretching, or the logo straight on a busy photo (put a Night panel behind it).</p>
    </div>
  </section>

  <section id="app">
    <span class="eyebrow">On the box</span>
    <h2>Icon, TV banner and boot animation</h2>
    <p class="lead">Generated from the same logo code, so they always match it. The icon is sunny so it stands out in Android's app lists; the banner sits in the TV launcher row.</p>
    <div class="app">
      <div>
        <h3>App icon</h3>
        <div class="icons">
          <figure><div class="i-l">{{ICON_L}}</div>168 px</figure>
          <figure><div class="i-m">{{ICON_M}}</div>96 px</figure>
          <figure><div class="i-s">{{ICON_S}}</div>48 px</figure>
        </div>
        <h3 style="margin-top:28px">TV banner (320&times;180)</h3>
        <img class="banner" src="{{BANNER}}" alt="Tundu TV banner: the logo on Night" width="320" height="180">
      </div>
      <figure style="margin:0">
        <h3>Boot animation</h3>
        <div class="tv"><img src="{{BOOT}}" alt="Boot animation: the figure jumps in, the letters pop in, then it keeps hopping above loading dots" width="640" height="360"></div>
        <figcaption class="cap">Plays while the box starts: the figure drops in and lands with a squash, the letters pop in one by one, then it keeps hopping until the games are ready. 1280&times;720, 30 fps, {{BOOT_KB}}.</figcaption>
      </figure>
    </div>
  </section>

  <section id="colour">
    <span class="eyebrow">Colour</span>
    <h2>Bright on Night</h2>
    <p class="lead">TV screens use Night, so colours glow without glare in a dim living room. Sun is the brand colour and marks whatever the remote or your hands have selected. Contrast ratios are against Night; 4.5:1 or more is safe for text.</p>
    <div class="swatches">
{{SWATCHES}}
    </div>
    <h3 style="margin-top:28px">Player colours (Bowling and other turn games)</h3>
    <div class="players">{{PLAYERS}}</div>
  </section>

  <section id="type">
    <span class="eyebrow">Type</span>
    <h2>Rounded, like the logo</h2>
    <p class="lead">Two free Google Fonts (SIL Open Font License) with every Albanian letter. Fredoka for titles and big numbers, Nunito for everything people read.</p>
    <div class="type">
      <div class="specimen">
        <div class="name">Fredoka &middot; titles, scores, buttons</div>
        <div class="d">K&euml;rce! Radha jote!</div>
        <div class="glyphs">&Euml; &euml; &Ccedil; &ccedil; Xh Gj Zh Sh 0123456789</div>
      </div>
      <div class="specimen">
        <div class="name">Nunito &middot; instructions, settings, body text</div>
        <p class="b" style="margin:0">Q&euml;ndro n&euml; mes, me gjith&euml; trupin brenda ekranit. Ngri t&euml; dyja duart lart p&euml;r t&euml; filluar loj&euml;n.</p>
        <div class="glyphs" style="font-family:var(--body);font-weight:800">&Euml; &euml; &Ccedil; &ccedil; &ldquo;Bravo!&rdquo;</div>
      </div>
    </div>
    <div class="table-scroll">
    <table class="scale">
      <tr><td>Display</td><td>96 px &middot; Fredoka 600</td><td style="font:600 44px/1 var(--display)">Bravo!</td></tr>
      <tr><td>Title</td><td>64 px &middot; Fredoka 600</td><td style="font:600 34px/1.1 var(--display)">Zgjidh loj&euml;n</td></tr>
      <tr><td>Heading</td><td>44 px &middot; Fredoka 500</td><td style="font:500 26px/1.2 var(--display)">Si luhet</td></tr>
      <tr><td>Body</td><td>34 px &middot; Nunito 600</td><td style="font:600 20px/1.4 var(--body)">L&euml;viz majtas e djathtas p&euml;r t&euml; nd&euml;rruar korsi.</td></tr>
      <tr><td>Small</td><td>26 px &middot; Nunito 700</td><td style="font:700 15px/1.4 var(--body);color:var(--mist)">Shtyp OK p&euml;r t&euml; vazhduar</td></tr>
    </table>
    </div>
    <p class="note">Sizes are for a 1080p TV seen from about 3 m; nothing smaller than 26 px. The right column shows each style at a reduced size.</p>
  </section>

  <section id="sample">
    <span class="eyebrow">Preview</span>
    <h2>How the home screen could feel</h2>
    <p class="lead">A sketch of the brand applied to the box's home screen (the real one comes in the UI phase).</p>
    <div class="screen">
      <div class="top">{{WHITE_SMALL}}<span class="hint">Ngri duart lart p&euml;r t&euml; luajtur</span></div>
      <div class="cards">
        <div class="card focus"><div class="art" style="background:var(--sun)">Vrapo!</div><b>Vrapo!</b><span>Kërce, ulu, ndërro korsi</span><span class="play">Luaj</span></div>
        <div class="card"><div class="art" style="background:var(--coral)">Fruta!</div><b>Fruta!</b><span>Copëto frutat me duar</span></div>
        <div class="card"><div class="art" style="background:var(--sky)">Bowling</div><b>Bowling</b><span>1&ndash;4 lojtarë</span></div>
      </div>
    </div>
  </section>

  <section id="files">
    <span class="eyebrow">Files</span>
    <h2>Where everything lives</h2>
    <ul class="files">
      <li><code>unity/MoveItGames/Assets/Resources/brand.json</code>: name, tagline, colours, fonts. The one file to edit.</li>
      <li><code>tools/branding/logo.py</code>: the logo geometry (SVG and pixels from the same numbers).</li>
      <li><code>tools/branding/make_branding.py</code>: regenerates everything below.</li>
      <li><code>brand/logo/</code>: logo, mark and icon as SVG and 2048 px PNG.</li>
      <li><code>Assets/Branding/</code>: icon, TV banner, in-game logo and mark.</li>
      <li><code>tools/branding/out/</code>: <code>bootanimation.zip</code> and <code>logo.bmp</code> for the box maker.</li>
    </ul>
  </section>
</div>
"""


def main():
    p = argparse.ArgumentParser(description="Write the brand board HTML page")
    p.add_argument("--out", type=Path, default=OUT / "brand-board.html")
    args = p.parse_args()
    brand = json.loads(BRAND_JSON.read_text(encoding="utf-8"))
    page = build(brand)
    els, box = logo.wordmark()
    page = page.replace("{{WHITE_ON_NIGHT}}", inline_svg(logo.svg(els, box, colors_on_dark(brand), pad=0.25)))
    page = page.replace("{{WHITE_SMALL}}", inline_svg(logo.svg(els, box, colors_on_dark(brand), pad=0.1)))
    args.out.write_text(page, encoding="utf-8")
    print(f"Brand board: {args.out} ({args.out.stat().st_size / 1024:.0f} KB)")


if __name__ == "__main__":
    main()
