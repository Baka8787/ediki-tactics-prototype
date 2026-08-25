using System;
using System.Collections.Generic;
using Ediki.Core;

namespace Ediki.Unity
{
    /// <summary>Whether an action needs something pointed at before it can resolve.</summary>
    public enum ActionTarget
    {
        /// <summary>Resolves on the actor. Nothing to aim.</summary>
        Self = 0,

        /// <summary>Needs an enemy inside its range.</summary>
        Enemy = 1,

        /// <summary>Previews an area and waits for a confirming board click.</summary>
        SelfConfirm = 2
    }

    /// <summary>
    /// One thing the selected unit can do this turn, as the UI needs to show it.
    ///
    /// Presentation only. It carries no rule: the cost and the range are read
    /// off UnitDef, and <see cref="Build"/> hands back one of the Commands that
    /// already exist. BattleSimulator remains the only thing that decides
    /// whether the action is legal (R-CMD-01) — a button being enabled is a hint,
    /// never a permission.
    /// </summary>
    public sealed class UnitAction
    {
        public string Label;
        public string Hint;
        public int ApCost;

        /// <summary>Cells away the target may be. 0 for self-targeted actions.</summary>
        public int Range;

        /// <summary>
        /// How far the effect spreads from wherever it lands. 0 = it hits one cell.
        ///
        /// Read off UnitDef like everything else here, and used for one thing:
        /// painting the footprint BEFORE the button is committed. An area skill
        /// whose shape only becomes visible after it fires is a skill the player
        /// has to learn by wasting it.
        /// </summary>
        public int AreaRadius;

        public ActionTarget Target;
        /// <summary>
        /// The key as a LABEL, not a KeyCode.
        ///
        /// Keeping the input device out of the descriptor is what lets this file
        /// be tested without an editor: which actions a unit gets, at what cost
        /// and what range, is the part that has to stay right as the roster
        /// grows, and it is pure algebra over UnitDef. BattleRunner maps the
        /// label onto a physical key in one place.
        /// </summary>
        public string ShortcutLabel;

        /// <summary>(actorId, targetId) -> command. targetId is ignored when Self.</summary>
        public Func<int, int, ICommand> Build;
    }

    /// <summary>
    /// The action list for a unit, derived entirely from its UnitDef.
    ///
    /// This is the answer to "the skill list is going to grow": a unit gains a
    /// button by having a cost for that skill in units.txt, and loses it by
    /// having 0. Nothing here has a roster in it, no id is ever named, and
    /// adding a fifth character with push and slow needs no code change at all —
    /// the same rule the rest of the project follows (A5).
    ///
    /// What DOES still need code is a genuinely new KIND of skill, because the
    /// Command for it has to exist in Ediki.Core first. That boundary is
    /// deliberate: the rule layer owns what a skill does, and this file owns
    /// only how it is offered.
    /// </summary>
    public static class BattleActions
    {
        public static List<UnitAction> For(UnitDef def)
        {
            List<UnitAction> actions = new List<UnitAction>();
            if (def == null) return actions;

            actions.Add(new UnitAction
            {
                Label = "攻擊",
                Hint = "對射程內的敵人造成 " + def.Atk + " 點攻擊力的傷害",
                ApCost = def.AttackApCost,
                Range = def.AttackRange,
                Target = ActionTarget.Enemy,

                ShortcutLabel = "A",
                Build = (actor, target) => new AttackCommand(actor, target)
            });

            actions.Add(new UnitAction
            {
                Label = "格擋",
                Hint = "到下個回合為止，受到的傷害減半",
                ApCost = def.GuardApCost,
                Target = ActionTarget.Self,

                ShortcutLabel = "G",
                Build = (actor, target) => new GuardCommand(actor)
            });

            if (def.CanRest)
                actions.Add(new UnitAction
                {
                    Label = "休息",
                    Hint = "回復 " + def.RestHealPercent + "% 生命，然後結束這個單位的行動",
                    ApCost = def.RestApCost,
                    Target = ActionTarget.Self,
                    ShortcutLabel = "H",
                    Build = (actor, target) => new RestCommand(actor)
                });

            if (def.CanPush)
                actions.Add(new UnitAction
                {
                    Label = "擊退",
                    Hint = "把目標往正後方推 1 格。推進深坑會直接消滅它",
                    ApCost = def.PushApCost,
                    Range = def.PushRange,
                    Target = ActionTarget.Enemy,
                    ShortcutLabel = "V",
                    Build = (actor, target) => new PushCommand(actor, target)
                });

            if (def.CanSlow)
                actions.Add(new UnitAction
                {
                    Label = "減速",
                    Hint = "目標這一輪每走一格多付 1 AP",
                    ApCost = def.SlowApCost,
                    Range = def.SlowRange,
                    Target = ActionTarget.Enemy,
                    ShortcutLabel = "F",
                    Build = (actor, target) => new SlowCommand(actor, target)
                });

            if (def.CanArmorBreak)
                actions.Add(new UnitAction
                {
                    Label = "破甲",
                    Hint = "目標防禦 -" + def.ArmorBreakAmount + "，持續一輪。給隊友製造機會用的",
                    ApCost = def.ArmorBreakApCost,
                    Range = def.ArmorBreakRange,
                    Target = ActionTarget.Enemy,
                    ShortcutLabel = "B",
                    Build = (actor, target) => new ArmorBreakCommand(actor, target)
                });

            if (def.CanTaunt)
                actions.Add(new UnitAction
                {
                    Label = "引誘",
                    Hint = "半徑 " + def.TauntRadius + " 內的敵人下一輪都會來打這個單位",
                    ApCost = def.TauntApCost,
                    AreaRadius = def.TauntRadius,
                    Target = ActionTarget.Self,
                    ShortcutLabel = "T",
                    Build = (actor, target) => new TauntCommand(actor)
                });

            if (def.CanPurify)
                actions.Add(new UnitAction
                {
                    Label = "淨化",
                    Hint = "清除半徑 " + def.PurifyRadius + " 內的穢氣",
                    ApCost = def.PurifyApCost,
                    AreaRadius = def.PurifyRadius,
                    Target = ActionTarget.SelfConfirm,
                    ShortcutLabel = "C",
                    Build = (actor, target) => new PurifyCommand(actor)
                });

            actions.Add(new UnitAction
            {
                Label = "待機",
                Hint = "保留剩下的 AP，結束這個單位的行動",
                ApCost = 0,
                Target = ActionTarget.Self,

                ShortcutLabel = "X",
                Build = (actor, target) => new WaitCommand(actor)
            });

            return actions;
        }
    }
}
