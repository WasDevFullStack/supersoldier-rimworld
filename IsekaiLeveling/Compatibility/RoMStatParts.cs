// RoMStatParts.cs — REMOVED
// 
// The StatPart_RoM_MaxMana, StatPart_RoM_ManaRegen, StatPart_RoM_MaxStamina,
// and StatPart_RoM_StaminaRegen classes were dead code. RimWorld of Magic does
// NOT expose StatDefs for mana/stamina (TM_MaxMana etc. don't exist). Instead,
// RoM uses Need_Mana and Need_Stamina (NeedDef system) with internal fields.
//
// Mana/stamina modifications are now handled via Harmony patches on Need_Mana
// and Need_Stamina in RoMHarmonyPatches.cs:
//   - NeedMana_MaxLevel_Postfix: INT + passive → max mana pool
//   - NeedStamina_MaxLevel_Postfix: VIT + passive → max stamina pool
//   - NeedMana_NeedInterval_Prefix/Postfix: WIS + passive → mana regen
//   - NeedStamina_NeedInterval_Prefix/Postfix: DEX + passive → stamina regen
