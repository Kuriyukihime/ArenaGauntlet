## 1.0.3

- **ArenaGauntlet has moved to GitHub.** This is the last update posted on Thunderstore. New versions will be at github.com/Kuriyukihime/ArenaGauntlet/releases.
  - To get notified, click **Watch → Custom → Releases** on that page.
  - To install a new version, import its ZIP in your mod manager (r2modman: Settings → Import local mod).
- Updating from Thunderstore? 1.0.2's multiplayer fixes below are new to you too.

## 1.0.2

- **Steadier multiplayer.** Fixes from a full audit of the host-to-client connection.
  - Players who take more than about 8 seconds to load into the arena no longer start the run without their career upgrades and loadout. The host now waits until they've finished loading.
  - A champion from another mod with a very long name no longer freezes the arena HUD for clients during that round.
  - Tested against a simulated bad connection: dropped, duplicated, delayed and damaged packets, late joins and players loading for up to four minutes.
- **Now on GitHub.** Releases, source and this changelog live at github.com/Kuriyukihime/ArenaGauntlet.

## 1.0.1

- **Higher Glory prices.** Upgrades and cosmetics cost about 50% more. Glory from rounds is unchanged.
  - Armory: 15 / 30 / 45 / 70 / 90 / 120 (was 10 / 20 / 30 / 45 / 60 / 80).
  - Barracks: 25 / 60 / 90 (was 15 / 40 / 60).
  - Patronage: 30 / 75 (was 20 / 50).
  - Treasury: 50 Glory each (was 30).
  - Every upgrade now totals 650 Glory instead of 430.
  - Everything you already own stays owned, and your unspent Glory is unchanged.
- **Tougher Feats.** Rewards are unchanged, and Feats you've already completed stay completed.
  - Favored of Olympus: win on Ascension III or higher (was I).
  - Unbroken: clear round 5 or later without falling (was any round).
  - Iron Will: clear 8 rounds in one run without falling in them (was 5).
  - Giant-Slayer: land the killing blow yourself. Companies, turrets and drones no longer count.
  - Myriad: defeat 400 enemies in one run (was 250).
  - Patron of the Agora: make 25 Bazaar purchases in one run (was 15).
  - Spoils of War: hold 60 items when a round ends (was 40).

## 1.0.0

- **Feats.** Twelve one-time challenges in a new Arena Career tab, worth 140 Glory in total.
  - Clear your first round (5), clear round 5 (10), win a run (20) and win on Ascension I or higher (25).
  - Clear a round without falling (10), and five rounds in one run without falling in them (15).
  - Land the killing blow on an arena champion (10) and defeat 250 enemies in one run (10). Your companies' kills count.
  - Lead a mercenary company (5) and command two at once (10).
  - Make 15 Bazaar purchases in one run (10) and hold 40 items when a round ends (10).
  - Glory is banked the moment you complete a Feat, even if the run is lost. The party sees your first completion in chat.
- **Treasury.** Spend Glory on cosmetics for the colosseum. Each costs 30 Glory and is yours to keep. Cosmetics never affect gameplay.
  - Three arena skins with matching floors: Gilded Basalt, Moonlit Sanctuary and Overgrown Ruins.
  - Five decoration sets you can show or hide: Arena Guardians, Champion Standards, Victory Braziers, Victors Gallery and Laurels of Renown.
  - In multiplayer, everyone sees the host's arena.
- **Item tooltips in Starting Loadout.** Hover over any item or equipment to see its full description and stacking stats. Equipment also shows its cooldown. With LookingGlass installed, items show its calculated stats.
- Resetting your career now also clears cosmetics and completed Feats. Feats can be earned again.
- The download is larger, about 37 MB, because of the new arena art.

## 0.39.0

- **Personal loot in multiplayer.** Every enemy drop and champion reward belongs to one player. Only that player can see it and pick it up.
  - Horde drops and champion bonus drops rotate evenly through the party.
  - Each player's round-clear reward is their own.
  - At round clear, uncollected drops go to their owner. If the owner can't take them, they follow that player into the next arena.
  - If a player leaves, their drops become available to everyone.
  - Shop purchases, scrapper output and lunar coins stay shared.
- **Endless mode is off by default.** Round 10 now ends the run in victory. Existing settings are switched off once. Turn it back on in the mod options under Progression > AllowEndlessContinuation.
- **Fewer horde drops.**
  - Solo: about 15 items per run instead of 25 (about 2.6% per kill instead of 4.3%).
  - Full party: about 19 per player instead of 29.
  - A typical run now totals about 73 items per player instead of about 82.
  - Gold, Bazaar prices and clear rewards are unchanged.

## 0.38.0

- Corrected the Ascension descriptions in Arena Career to match the actual challenge. Each rank now lists its horde health bonus and how many marked strikes arrive and how often. Strikes give 2.5 seconds to move, not two. Ranks V and VI no longer promise faster strikes or more champions than they add.

## 0.37.0

- Arena Career Glory and upgrades are now saved once for the whole game. They carry over between updates, reinstalls and every mod-manager profile. Your existing Glory is imported automatically from your old profile saves.
- Added a Reset Career button to Arena Career's Permanent Upgrades page. A confirmation screen shows exactly what you'll lose before anything is erased.
- The objective now shows the arena's actual goal: prepare, defeat the horde or champions, then shop and ready up. It replaces Simulacrum's "Activate the Focus" and "Survive..".
- Fixed the War Council status line being cut off.
- Long item, boss and theme names, including modded ones, now shrink to fit menus and the HUD instead of being cut off.

## 0.36.0

- Fixed the next round never starting if a player was dead while shopping. Dead players no longer block the ready brazier and revive when the round begins.
- Fixed the ready brazier not appearing if a merchant stall had nothing to stock, for example with lunar items disabled.
- Fixed errors from other mods during champion rewards keeping the merchants closed after a round.
- Paired champions on rounds 6-9 now pick two different species, as intended.

## 0.35.0

- Fixed multiplayer clients not seeing round status and progress. Only the host saw it.
- Fixed enemy health bars not matching for clients.
- Fixed the merchant alcoves only opening for the host after a round.
- Fixed clients being unable to hire War Council companies, and their Arena Career perks and Glory not applying.

## 0.34.0

- Fixed hordes endlessly retrying setup and never spawning when playing with WolfoQualityOfLife.

## 0.33.0

- Fixed the entire HUD failing to load when playing with WolfoQualityOfLife.
- Placed Arena Career directly below Ready, with matching width and a small gap.
- Earn permanent Glory from cleared rounds, including unfinished runs. Spend it through Arena Career, available only on ArenaGauntlet's survivor-selection screen.
- Armory lets you choose starters, then unlock equipment, lunar items, reds and extra slots. At maximum, bring five items plus equipment with a reusable 21-point budget and up to two reds. Eligible modded items are supported.
- Barracks grants a free opening company, an extra commander slot per player and one automatic fallen-commander recovery per run. Existing company damage is unchanged.
- Patronage grants an extra free white purchase each break, upgraded to white or green. It costs no gold and never uses your normal round item allowance.
- Unlock six optional Ascensions with tougher champions, marked arena strikes and additional ordinary champions. Horde population caps and clear gold stay unchanged. Full clears award 20 Glory on Standard or 25-50 on Ascension.
- Career saves persist per game profile. MetaMarket can stack with ArenaGauntlet progression.

# 0.32.0

- Reduced later hordes to finish at **80 enemies solo**, or **120/160/200 with 2/3/4 players**. Rounds 1-4 and the 80-enemy live limit are unchanged.
- Reduced the enemy spawning budget and horde loot in proportion to the smaller hordes. XP and renown keep pace; **round-clear gold and shop prices are unchanged**.
- Rebalanced all six tier III-V companies and their squads. Tough ranged units, tanks and buffers trade some damage for their durability and support. Tiers I-II, health, squad sizes, hire costs and unlock rounds are unchanged.
- Reduced Clay Templar firepower and Void Jailer salvos, including the Jailers in Abyssal Siege. Lunar Vanguard and Solus Directive also deal less damage; Solus's extra summoned Probes now receive the company's damage adjustments.
- Hired Titans can use their laser while healthy instead of waiting to lose health.
- Adjusted higher-tier company damage for the smaller per-player allowance in co-op, including party changes and late joiners.

# 0.31.0

- Shortened later hordes to finish at 80 enemies solo, or 120/160/200 with 2/3/4 players. Rounds 1-4 and the 80-enemy live cap are unchanged.
- Director starting credits, credit income and horde loot fall by the same percentage as enemy count, keeping the smaller fights from filling with extra elites.
- Smoothed tier III-V company damage into steady tier-by-tier increases, reducing commander damage by about 11% / 18% / 33% and squad damage by 7% / 13% / 25%.
- Higher tiers retain larger damage bonuses. Company health and tiers I-II are unchanged, and XP/renown pacing is preserved with fewer enemies.

# 0.30.1

- Refreshed the mod description.

# 0.30.0

- Every player starts the run with three random white or green items, including eligible modded items.
- Shop between rounds at five merchant alcoves inside the arena instead of travelling to the Bazaar. BiggerBazaar is no longer required.
- Merchants sell items, lunar items, equipment and drones, with six choices for items, lunar items and equipment. A scrap trader and a scrapper are also available.
- Each player can buy three whites, two greens and one red per break. Trade one green or red scrap for the matching offered item.
- When you finish shopping, ready up at the brazier. An arrow shows the way, the HUD shows how many players are ready, and dead teammates return for the break.
- Loot is retuned for about 80-90 items per player by Mithrix. Horde drops increase through the rounds, elites drop items more often, and co-op players keep close to the solo pace. Summoned enemies no longer drop arena loot.
- Round-clear gold is about 14% higher to help pay for equipment and drones.
- Enemy kills grant 50% more experience.

# 0.22.0

- Added new arena-battle cover artwork.
- Updated the how-to-play description and clarified that hosting requires Survivors of the Void.

# 0.21.0 - Initial release

- Ten rounds in a Greek stone arena, with major bosses on 5 and 10 and paired champions on ordinary rounds 6-9.
- Between-round shopping and an optional harder continuation cycle.
- Ten allied companies with limits of three solo, two per player with 2-3 players, or one with 4 or more.
- Renown from kills and champions, clear gold for roughly three average item purchases, and increased item drops for the shorter run. Enemies give no base gold.
- Multiplayer improvements for joining, commander purchases and arena loading. Excess companies refund their hire cost when a larger party lowers the cap.
