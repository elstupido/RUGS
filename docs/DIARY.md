# RUGS! — Dev Diary

The messier, more human side of building RUGS! — the decisions, the wrong turns, the wins. Newest first.

---

## 2026-07-03 — the flywheel

This one started with the itch that wouldn't quit: what IS the endgame? Three research dives into what Big
Ambitions' richest players actually do all came back with the same answer — there isn't one. The devs admit
it. Players hit $5M and go numb. And the boss stared straight into that hole and flipped it inside out: if
the base game's late-game is "own 150 businesses, pointlessly," then give the grind a POINT — you own 150
businesses *to launder your drug money*. The complaint IS the content. Cleanest design inversion this
project has produced.

So the endgame is a balance beam: dirty intake on one side, wash capacity on the other, factories multiplying
both at the same rate so getting huge never gets easy — just bigger. The GL made it visible: a green-phosphor
accounting terminal with the balance verdict sitting dead center, books on the left, levers on the right. And
the night crew made it playable at scale, because pressing "wash" thirty times a night isn't gameplay, it's
data entry.

The review fleet earned its keep too. It caught a street-offer money printer (buy the discounted lot with
full hands, "no room," flip it at full price — oops) and a factory exploit where an unmanned delivery plan
handed out free multipliers. Both dead before a single player ever met them. That's the difference between
shipping fast and shipping right — this time we did both.

1.5.0 goes up. The mod finally IS the thing we kept calling it: Drug Wars inside Big Ambitions, down to the
uniform price rolls. The boss is starting a fresh $100k save to ride the on-ramp himself. Corner kid to
kingpin. Wash everything.

*Postscript, launch day: front page of BA mods.* The washer-count doctrine applies to feelings too —
sometimes the books just balance.

---

## 2026-06-28 — the slippery slope, and where we planted the flag

Started simple: "another pass over events — pull in everything we can from Drug Wars." Then the boss found the
trapdoor. If we're doing branching choices, why not combat? And if combat, why not render it in glorious text-art
with drawille? It's a real vision, and an honest one — that's exactly the game Drug Wars was. But it's a different
game than the one we've shipped. So we did the thing this project keeps rewarding: we stopped and named the slope
before sliding down it.

Two pillars settled it. Reuse over invention — combat is a brand-new simulation with nothing in BA to lean on.
And the fiction — RUGS! deliberately has no cops; the taxman is the only law. A cop shootout wouldn't be "more
Drug Wars," it'd undo the very reskin the mod is built on. So: branching choices, yes (small, clean, routes into
outcomes we already had). Combat, no. And drawille — too fun to bin — lives as pure aesthetic: a monospace,
green-on-black terminal skin with box-drawing headers. The boss saw it in-engine: "looks great." That one landed.

The flavor lines were the careful bit. The boss wanted the genuine dopewars subway-sayings, not anything I'd make
up — fair, and right. So they're the real article, credited to dopewars under the GPL. I kept the timeless absurd
ones verbatim and just left out the few that were drug-specific or stuck in 1992, rather than find-and-replacing
them into the rug world. Cleaner to omit than to launder.

And a small honest no: the trenchcoat. Carrying more is iconic Drug Wars, but BA gates box capacity at the item
level, so a carry upgrade would've quietly tripled your cart and storage too — straight through the scarcity that
makes hauling mean something. Parked it, shipped the cheap-lot offer instead, and called the deferral out loud.
Out the door as 1.4.0.

---

## 2026-06-27 — the part where you stop and think

Phase A came together and, for once, just *worked* — first try in-engine. You hire a dealer at a shop you
already own, and the next morning his cut is sitting there, sized to how much real business the place did.
Watching the dirty cash tick up off a number BA booked for its own reasons — money we never touched, just
read — is the whole sidecar idea paying off. The road to that design was a winding one (we hit the Harmony
wall, said "if it needs Harmony we don't do it," and the boss cracked it with "ride along"), so seeing it
click felt earned.

Then Phase B, and the smart move turned out to be *not* building it. We sat with three questions — where the
product comes from, how you move it, and what the whole thing is even for — and the reuse instinct paid off
again: a dealer's inventory isn't a new system, it's the rugs you physically haul in and stash at the shop,
which the game already lets you do. And the end-game isn't a trophy; it's "how big a dirty operation can you
keep clean before the taxman notices." Then the boss said "I'm not satisfied, I need to think about this more"
— and that's the right call. The best decisions on this thing have all come from refusing to rush.

A good bug, too: gifting rugs to someone pushing a cart was quietly broken — the game empties your hands when
you grab a cart, so the old code tried to jam a box into hands that were busy. Now it finds the cart, fills
it, and if there's no room anywhere it sells the gift for dirty cash instead of dropping it on the floor. A
small fix, but it's the kind of edge a real QA pass surfaces — fitting, since part of today went to reading a
QA engineer's save off disk (in-game Day 1; he'd barely gotten started).

---

## 2026-06-26 — riding along

Slow morning, water instead of coffee, and we sat down to pick the next rung on the ladder. Picked two: ship
the little onboarding kindness (First Ones Free) and finally design T2 — your own dealers.

T0.1 was the easy joy. A broke player walks up to their first dealer and gets a stash fronted to them — "first
taste's on me." It's maybe forty lines, almost all of it reusing machinery we already had: the find-stash
grant, the arrival panel. The kind of feature that's pure upside and takes an hour. Built, clean, deployed.

T2 was the real conversation, and it went somewhere I didn't expect. The instinct was "reskin a fruit shop" —
reuse over invention, like we keep promising ourselves. But the deeper I dug into BA's retail guts, the harder
a wall got: every business books its money to the one clean, taxed wallet through a *private* method we can't
touch without Harmony — and Harmony's off the table. So a fruit shop that reuses BA's sim makes *clean* money,
full stop. We wanted dirty. Impasse. I said so and stopped, because that's the rule we set: if it needs
Harmony, we don't do it.

Then the boss cracked it in one message: a **sidecar**. Don't fight BA for the money — *ride along*. Let the
legit business do its honest thing, watch what it deposits, and mint dirty cash on top as a multiple of that.
We never touch BA's wallet; we just read the receipt. The dirty money is brand-new money we add, proportional
to how busy the front is. Obvious in hindsight, the way the good ones always are.

It kept getting better. Not a rented fake front — a dealer in the back of a business you *already own*. And
one clean rule that did a surprising amount of work: a business either sells or launders, never both. That one
sentence killed a feedback-loop worry I'd been fretting about and turned the whole empire into an allocation
game — earners minting, washers cleaning, you balancing the two as you scale.

Didn't build T2 today — just designed it, honestly, in phases, with the hard parts named out loud. But it's
the first time the road past the street hustle feels real instead of hand-wavy. Good morning's work.
