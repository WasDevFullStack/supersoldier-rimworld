using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Handles golden XP and level up visual effects
    /// </summary>
    public static class IsekaiAnimations
    {
        // Golden colors for XP effects
        private static readonly Color XPGoldLight = new Color(1f, 0.9f, 0.4f, 1f);
        private static readonly Color XPGoldDark = new Color(0.95f, 0.75f, 0.2f, 1f);
        private static readonly Color LevelUpGold = new Color(1f, 0.85f, 0.3f, 1f);
        private static readonly Color LevelUpWhite = new Color(1f, 1f, 0.9f, 1f);
        
        /// <summary>
        /// Show golden XP gain floating text with sparkle effect
        /// </summary>
        public static void PlayXPGainEffect(Pawn pawn, int amount, string source = null)
        {
            if (pawn == null || !pawn.Spawned) return;
            
            Map map = pawn.Map;
            Vector3 pos = pawn.DrawPos;
            
            // Main golden XP text with source if available
            string xpText;
            if (!string.IsNullOrEmpty(source))
            {
                xpText = $"+{NumberFormatting.FormatNum(amount)} XP ({source})";
            }
            else
            {
                xpText = $"+{NumberFormatting.FormatNum(amount)} XP";
            }
            MoteMaker.ThrowText(pos + new Vector3(0f, 0f, 0.5f), map, xpText, XPGoldLight, 2.5f);
            
            // Small sparkle particles around the pawn
            for (int i = 0; i < 3; i++)
            {
                Vector3 sparkleOffset = new Vector3(
                    Rand.Range(-0.4f, 0.4f),
                    0f,
                    Rand.Range(-0.2f, 0.6f)
                );
                FleckMaker.ThrowLightningGlow(pos + sparkleOffset, map, 0.3f);
            }
        }
        
        /// <summary>
        /// Show small XP gain for minor activities (less intrusive)
        /// </summary>
        public static void PlaySmallXPEffect(Pawn pawn, int amount)
        {
            if (pawn == null || !pawn.Spawned) return;
            
            // Just a simple floating number for small gains
            Vector3 offset = new Vector3(Rand.Range(-0.3f, 0.3f), 0f, Rand.Range(0.3f, 0.5f));
            MoteMaker.ThrowText(pawn.DrawPos + offset, pawn.Map, $"+{NumberFormatting.FormatNum(amount)} XP", XPGoldDark, 1.5f);
        }
        
        /// <summary>
        /// Play epic level up effect with golden burst
        /// </summary>
        public static void PlayLevelUpEffect(Pawn pawn, int newLevel)
        {
            if (pawn == null || !pawn.Spawned) return;
            
            Map map = pawn.Map;
            Vector3 pos = pawn.DrawPos;
            
            // Large golden glow burst
            FleckMaker.Static(pos, map, FleckDefOf.PsycastAreaEffect, 3f);
            
            // Multiple light particles radiating outward
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector3 particlePos = pos + new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f);
                FleckMaker.ThrowLightningGlow(particlePos, map, 0.8f);
            }
            
            // Inner ring of sparkles
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                Vector3 sparklePos = pos + new Vector3(Mathf.Cos(angle) * 0.3f, 0f, Mathf.Sin(angle) * 0.3f);
                FleckMaker.ThrowLightningGlow(sparklePos, map, 0.4f);
            }
            
            // Big level up text
            MoteMaker.ThrowText(pos + new Vector3(0f, 0f, 0.8f), map, "Isekai_LevelUpMote".Translate(), LevelUpWhite, 4f);
            
            // Level number below
            MoteMaker.ThrowText(pos + new Vector3(0f, 0f, 0.4f), map, "Isekai_LevelMote".Translate(newLevel), LevelUpGold, 3.5f);
        }
        
        /// <summary>
        /// Play effect when spending stat points
        /// </summary>
        public static void PlayStatUpEffect(Pawn pawn, string statName)
        {
            if (pawn == null || !pawn.Spawned) return;
            
            Vector3 pos = pawn.DrawPos;
            
            // Small sparkle
            FleckMaker.ThrowLightningGlow(pos, pawn.Map, 0.4f);
            
            // Stat up text
            MoteMaker.ThrowText(pos + new Vector3(0f, 0f, 0.5f), pawn.Map, $"{statName} +1", XPGoldLight, 2f);
        }
        
        /// <summary>
        /// Play effect when learning a new perk/skill
        /// </summary>
        public static void PlayPerkUnlockEffect(Pawn pawn, string perkName)
        {
            if (pawn == null || !pawn.Spawned) return;
            
            Map map = pawn.Map;
            Vector3 pos = pawn.DrawPos;
            
            // Moderate glow effect
            FleckMaker.Static(pos, map, FleckDefOf.PsycastAreaEffect, 1.5f);
            
            // Sparkles
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = new Vector3(Rand.Range(-0.4f, 0.4f), 0f, Rand.Range(-0.2f, 0.5f));
                FleckMaker.ThrowLightningGlow(pos + offset, map, 0.5f);
            }
            
            // Perk name
            MoteMaker.ThrowText(pos + new Vector3(0f, 0f, 0.6f), map, $"Learned: {perkName}", new Color(0.6f, 0.9f, 1f, 1f), 3f);
        }
        
        /// <summary>
        /// Play dramatic summoning VFX when the world boss spawns wave minions.
        /// Large psycast-style radial shockwave centered on boss + expanding lightning rings
        /// + camera shake + per-minion spawn flash.
        /// </summary>
        public static void PlayWorldBossSummonEffect(Pawn boss, List<Pawn> summonedMobs)
        {
            if (boss == null || !boss.Spawned) return;

            Map map = boss.Map;
            Vector3 center = boss.DrawPos;

            // === CAMERA SHAKE — the ground trembles ===
            Find.CameraDriver.shaker.DoShake(4f);
            Find.CameraDriver.shaker.SetMinShake(3f);

            // === CENTRAL SHOCKWAVE — large psycast burst on boss ===
            FleckMaker.Static(center, map, FleckDefOf.PsycastAreaEffect, 6f);

            // === EXPANDING RING 1 — inner energy ring (16 points) ===
            for (int i = 0; i < 16; i++)
            {
                float angle = i * 22.5f * Mathf.Deg2Rad;
                Vector3 ringPos = center + new Vector3(Mathf.Cos(angle) * 2f, 0f, Mathf.Sin(angle) * 2f);
                FleckMaker.ThrowLightningGlow(ringPos, map, 1.2f);
            }

            // === EXPANDING RING 2 — outer energy ring (24 points) ===
            for (int i = 0; i < 24; i++)
            {
                float angle = i * 15f * Mathf.Deg2Rad;
                Vector3 ringPos = center + new Vector3(Mathf.Cos(angle) * 4.5f, 0f, Mathf.Sin(angle) * 4.5f);
                FleckMaker.ThrowLightningGlow(ringPos, map, 0.8f);
            }

            // === RADIAL STREAKS — energy lines from center outward ===
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                for (float dist = 0.5f; dist <= 5f; dist += 0.7f)
                {
                    Vector3 streakPos = center + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                    FleckMaker.ThrowLightningGlow(streakPos, map, Mathf.Lerp(0.6f, 0.2f, dist / 5f));
                }
            }

            // === PER-MINION SPAWN FLASH — each summoned creature gets a smaller burst ===
            if (summonedMobs != null)
            {
                foreach (Pawn mob in summonedMobs)
                {
                    if (mob == null || !mob.Spawned) continue;
                    Vector3 mobPos = mob.DrawPos;
                    FleckMaker.Static(mobPos, map, FleckDefOf.PsycastAreaEffect, 1.5f);
                    for (int j = 0; j < 6; j++)
                    {
                        float angle = j * 60f * Mathf.Deg2Rad;
                        FleckMaker.ThrowLightningGlow(
                            mobPos + new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f),
                            map, 0.5f);
                    }
                }
            }

            // === BOSS ROAR TEXT ===
            MoteMaker.ThrowText(center + new Vector3(0f, 0f, 1.2f), map,
                "Summoning!", new Color(1f, 0.3f, 0.3f, 1f), 4f);

            // === SOUND — psycast-style warp noise ===
            if (SoundDefOf.Psycast_Skip_Exit != null)
            {
                SoundDefOf.Psycast_Skip_Exit.PlayOneShot(SoundInfo.InMap(new TargetInfo(boss.Position, map)));
            }
        }

        /// <summary>
        /// Play effect when gaining a new title
        /// </summary>
        public static void PlayTitleGainEffect(Pawn pawn, string titleName, Color titleColor)
        {
            if (pawn == null || !pawn.Spawned) return;
            
            Map map = pawn.Map;
            Vector3 pos = pawn.DrawPos;
            
            // Royal-like effect
            FleckMaker.Static(pos, map, FleckDefOf.PsycastAreaEffect, 2f);
            
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                Vector3 particlePos = pos + new Vector3(Mathf.Cos(angle) * 0.4f, 0f, Mathf.Sin(angle) * 0.4f);
                FleckMaker.ThrowLightningGlow(particlePos, map, 0.6f);
            }
            
            // Title text
            MoteMaker.ThrowText(pos + new Vector3(0f, 0f, 0.7f), map, "New Title!", LevelUpWhite, 3f);
            MoteMaker.ThrowText(pos + new Vector3(0f, 0f, 0.3f), map, titleName, titleColor, 3.5f);
        }
    }
}
