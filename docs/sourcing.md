# Sourcing the Tundu console: what to buy, where, and the process

Checked **2026-10-08** from Albania (AliExpress showed prices in lek and free shipping to Albania; €1 ≈ 92 L).
Listings change fast: open the link, pick the right variant (**4 GB / 32 GB, EU plug**), and check the spec table and
seller rating, not just the title. Prices are the listing price for the cheapest variant shown, before VAT.

## What a Tundu console kit contains

| Part | Spec that matters | Pilot (AliExpress, 1 piece) | Batch 1 (Alibaba / ODM, 100–300) |
|---|---|---|---|
| **Box** | Rockchip **RK3566** (or RK3576), **4 GB RAM**, 32 GB, Android 11+, USB 3.0, Wi-Fi 5/6, HDMI 2.0 | €52–113 | €25–45 (RK3566) |
| **Camera** | **UVC** (driver-free), MJPEG, 1080p30 or better (60 fps is a bonus), **100–120° diagonal**, not fisheye | €17–78 | €13–24 |
| TV clip mount | Fits on top of a thin TV, tilt, 1/4" screw | €3–6 | €1–2 |
| USB extension | USB 3.0, 1.5–2 m, male to female (camera on the TV, box behind it) | €3–5 | €1 |
| Remote, HDMI cable, power supply (**EU plug**) | Come with the box. Ask the ODM for our logo on the remote | included | included |
| Retail box + insert | Printed box, cardboard or foam insert holding box, camera, remote, cables | — | €0.50–2 |
| Quick-start card, warranty card | Albanian, with the importer's name and address, CE and WEEE (crossed-out bin) marks | print locally | €0.30 |

## 1. Order the samples now (2–4 weeks to Albania)

Delivery estimates shown on the listings today: **20 Oct – 8 Nov**.

| # | Item | Link | Price | Why this one |
|---|---|---|---|---|
| 1 | **RK3566 box: Tanix TX66 4G/32G** | [aliexpress.com/item/1005005643923191](https://www.aliexpress.com/item/1005005643923191.html) | 7,111 L (~€77) | Real brand (Tanix), 663 sold, store 100% positive. Android 11 |
| 1b | alternative: X98K Pro RK3566 4G/32G | [aliexpress.com/item/1005008943382146](https://www.aliexpress.com/item/1005008943382146.html) | 6,854 L (~€74) | 16 sold, 5★, external antennas |
| 1c | cheapest: H96 MAX V56 RK3566 | [aliexpress.com/item/1005012576542612](https://www.aliexpress.com/item/1005012576542612.html) | 4,827 L (~€52) | New listing (2 sold): riskier, "H96" boxes vary in chips |
| 2 | **RK3576 box: H96 MAX M9 4G/32G** | [aliexpress.com/item/1005007470957283](https://www.aliexpress.com/item/1005007470957283.html) | 10,384 L (~€113) | Most sold RK3576 listing (54), 4.2★, free shipping, Android 14, 6 TOPS NPU. Pick **EU plug** |
| 2b | alternative: H96 MAX M9S 4G/32G | [aliexpress.com/item/1005013081388645](https://www.aliexpress.com/item/1005013081388645.html) | 8,078 L (~€88) | Same chip, newer listing |
| 3 | **Camera: Spedal 931, 120° wide, enclosed, with clip** | [aliexpress.com/item/1005005073100759](https://www.aliexpress.com/item/1005005073100759.html) | 5,368 L (~€58) | 455 sold, 4.5★. Check the 120° is diagonal (horizontal 120° would bend the body at the edges) |
| 4 | Camera (optional): **ELP OV4689, 1080p 60 fps, 110° no distortion** | [aliexpress.com/item/1005012478362517](https://www.aliexpress.com/item/1005012478362517.html) | 7,186 L (~€78) | Known UVC maker. 60 fps = ~17 ms less delay. Board without a case: the candidate for our own console later |
| 4b | cheap test camera: REDEAGLE 110° 2K module | [aliexpress.com/item/1005007506232264](https://www.aliexpress.com/item/1005007506232264.html) | 1,541 L (~€17) | Board only; good enough to test framing at 2–2.5 m |
| 4c | low-light option: IMX307 starlight USB module, 60 fps | [aliexpress.com/item/1005009734265700](https://www.aliexpress.com/item/1005009734265700.html) | (check) | Evening living rooms are the main tracking risk |
| 5 | TV clip mount (no drilling) | [aliexpress.com/item/1005012072806753](https://www.aliexpress.com/item/1005012072806753.html) | ~€5 | Only if the camera's own clip doesn't hold on your TV |
| 6 | USB 3.0 extension 2 m | [search](https://www.aliexpress.com/w/wholesale-usb-3.0-extension-cable-2m.html) | ~€4 | Any with good reviews |

**Recommended order (updated 2026-10-09): 1 + 2 + 4 + 6 ≈ 25,000 L (~€272), ~€326 with VAT at pickup.** The ELP
60 fps camera replaces the Spedal: it also runs at 30 fps, so one camera answers both "is a normal 30 fps webcam
enough?" and "how much does 60 fps help smooth hand tracking?", and it is the camera candidate for the v2 console.
The Spedal isn't the batch camera either (that's an OEM logo webcam, €13–16): get a sample of that one with the
batch quotes. Boxes cost more than the €150–190 in the original plan: Rockchip prices went up with the 2025–26 RAM
shortage.

**Avoid** (they appear in the same searches): Allwinner H313/H618/H728, Amlogic S905W, Rockchip **RK3528 / RK3518 /
RK3539** (quad A53, weak GPU, no NPU), anything titled "8K, 16 GB" for €20.

### Customs on the samples
- Online purchases sent by post are **duty-free up to €22**; above that the parcel goes through a simplified
  customs declaration ([dogana.gov.al](https://dogana.gov.al/english/c/226/online-shopping)). Expect to pay
  **20% VAT** on the boxes and cameras when you collect them. Keep the order PDFs.
- Use **guest Wi-Fi** for the boxes (cheap boxes have shipped with malware), then follow the Phase 1 checklist in
  `docs/hardware.md`.

## 2. Batch 1 suppliers (Alibaba, after the samples pass)

Alibaba prices shown in lek, per unit, by quantity. Use **Trade Assurance** (pay through Alibaba, not by bank
transfer to the seller). Ask every supplier the same questions (section 3).

| What | Supplier / listing | Price | MOQ |
|---|---|---|---|
| RK3566 box, custom logo + firmware | Shenzhen Daqu: [H96 MAX RK3566, "Low MOQ Custom Logo Firmware"](https://www.alibaba.com/product-detail/Low-MOQ-Custom-Logo-Firmware-Android_1600316864383.html) | 2,799–4,109 L (€30–45) | 1 |
| RK3566 box, OEM/ODM | Shenzhen Haochuangyi (13 yrs): [MXG PRO RK3566](https://www.alibaba.com/product-detail/Custom-OEM-ODM-High-Quality-MXG_1600715935037.html) | 936–4,446 L | 1 set |
| RK3566 box 4/32 | [X98K Pro RK3566](https://www.alibaba.com/product-detail/X98K-Pro-RK3566-Android-11-Smart_1601519426279.html) (100 sold) | 2,977–3,089 L (€32–34) | 10 |
| RK3566 box 4 GB | [HK1 Rbox R3 RK3566](https://www.alibaba.com/product-detail/TV-Box-Powerful-RK3566-4GB-DDR4_1601652186044.html) | 2,340 L (€25) | 100 |
| RK3576 box 4/32 | Shenzhen Young-Feel: [RK3576 Android 14](https://www.alibaba.com/product-detail/RK3576-4GB-32GB-Original-Rk3576-Tv_1601341591571.html) | 9,173 L (€100) single piece | 1 |
| Webcam, OEM logo | Hampo: [OEM 1080p wide-angle webcam](https://www.alibaba.com/product-detail/Hampo-Factory-Supply-OEM-1080p-Wide_1601158026008.html) | 1,170–1,451 L (€13–16) | 1 |
| Camera module 120° (for our own case later) | [2MP 1080p USB module, 120°, customizable](https://www.alibaba.com/product-detail/Source-Factory-Can-Be-Customized-2MP_1601950990767.html) | 2,247 L (€24) | 1 |
| Retail box with foam insert, custom size and print | [flip-top magnetic box with insert](https://www.alibaba.com/product-detail/Flip-Top-Magnetic-Box-with-Foam_1601454408898.html) | from 47 L (€0.50) | 100 |
| Printed corrugated box (cheaper look) | [custom printed shipping box](https://www.alibaba.com/product-detail/Custom-Printed-Corrugated-Shipping-Boxes-for_1601559393076.html) | from 51 L | 500 |

Most box ODMs also print the retail box themselves; ask for a quote with and without packaging.

**Unit cost at batch 1 (RK3566 tier):** box €25–34 + camera €13–24 + clip, cable €2–3 + packaging and cards €1–2
= **€41–63** before shipping; ~**€53–82 landed** with freight, customs and VAT. A €99 retail price works with the
RK3566. The RK3576 (~€60–100 in bulk) only fits at €129–149: one more reason the RK3566 benchmark decides a lot.

## 3. The process, step by step

1. **This week: order the samples** (section 1). While they travel, keep building games on PC and MuMu.
2. **Test the samples** (Phase 1 checklist in `docs/hardware.md` and `docs/box-os.md`): camera visible to Android,
   pose ≥ 25 fps *while* a game renders, latency < 150 ms, camera survives reboot and replug, remote key codes, ALLM.
   This picks the tier.
3. **Request quotes** from 3–5 ODMs with one message: the box spec, the layer-3 firmware list from
   `docs/box-os.md` (our boot logo and animation, Tundu as the only home app, no Google/IPTV/OTA junk, clean
   firmware, external camera HAL on), logo on the shell and remote, our packaging. Ask for: price at 100 / 300 / 500,
   firmware customization fee, lead time, warranty and dead-on-arrival rate, and **CE documents for the exact model**
   (RED for Wi-Fi/Bluetooth, EMC, LVD for the power adapter, RoHS).
4. **Golden samples:** order 2–3 units with our firmware and logo through Trade Assurance. Approve them in writing.
5. **Batch order** (100–300) through Trade Assurance. Shipping: express/air (DHL, FedEx, UPS, or an air forwarder to
   Tirana) is simplest at this size (~1 kg per kit, 100–300 kg). Sea freight to Durrës only pays off for larger runs.
6. **Import as a business:** registration at **QKB** (NIPT), a **customs broker** (*agjent doganor*) clears the
   goods: duty (depends on the HS code; boxes and cameras are classed separately) + **20% VAT**. Ask your accountant
   whether to register for VAT (it decides whether import VAT is recoverable).
7. **Compliance:** Albania uses the **CE** mark (no national mark) on EU-aligned rules for radio equipment, EMC and
   low voltage, regulated by **AKEP** for anything with Wi-Fi/Bluetooth. AKEP Decision 721/2023 sets the labelling
   rules (label on the manual or packaging). As the importer, your **name and address must be on the packaging**,
   with instructions and safety notes in Albanian, and the documents from the ODM kept on file.
8. **Kit assembly in Tirana:** provision each box (`tools/provision/provision.ps1`, ~3 min per box), test it with
   its camera for one minute, pack: box, camera, clip, cables, remote, quick-start card, warranty card.
9. **Pilot families** (10–20, deposits / cash on delivery), then Batch 1 for **1 June** (Children's Day).

## 4. Later: a real all-in-one console (v2)

Nex Playground and Kinect are one bar on top of the TV: camera and computer together, one HDMI cable. Off-the-shelf
Android "camera bars" exist, but not at our price or chip yet: Videostrong's
[KA2 AI camera (Amlogic S905X4, Android 10)](https://www.alibaba.com/product-detail/Videostrong-KA2-AI-Camera-Amlogic-S905X4_1600784678161.html)
is ~€100 at MOQ 3,000, and [Android all-in-one conference cameras](https://www.alibaba.com/product-detail/1080p-HD-4k-Eptz-All-in_1600774476433.html)
are ~€215 at MOQ 500. The realistic v2 is our own case around an RK3566/RK3576 board and a camera module like
the ELP or the 120° module above: 3D-printed prototypes first, an injection mold (€3k–15k) only after Batch 1 sells.
Ask the Batch 1 ODM early whether they make RK3566/RK3576 boards for custom enclosures.

## Sources
- AliExpress and Alibaba listings linked above (opened 2026-10-08).
- Albanian customs, online shopping: <https://dogana.gov.al/english/c/226/online-shopping>
- AKEP Decision 721/2023 (radio equipment labelling), summary: <https://gma.caict.ac.cn/en/update/77/367>
- Albania uses CE, no national mark (GTAI): <https://www.gtai.de/de/trade/albanien/zoll/standards-und-normen-764168>
- Lek/euro rate (~91.6–92 L per € on 1 Oct 2026): <https://www.exchange-rates.org/exchange-rate-history/all-eur-2026-10-01>
- H96 Max M9 (RK3576) background: <https://androidpctv.com/?p=43322>
