using System;
using System.Collections;
using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ediki.Unity
{
    /// <summary>
    /// Drives one playable battle: turns input into Commands, plays the resulting
    /// EffectLog, and runs the enemy phase.
    ///
    /// Every state change goes through BattleSimulator.Execute (R-CMD-01). This
    /// class holds the current BattleState reference only to pass it back in and
    /// to feed read-only queries; it never mutates it.
    /// </summary>
    public sealed class BattleRunner : MonoBehaviour
    {
        private const float EnemyStepSeconds = 0.35f;
        private const int MaxEnemyCommandsPerUnit = 16;

        private BattleSetup _setup;
        private BattleState _state;
        private BattleView _view;
        private EnemyAi _ai;
        // Set by Initialise, NOT by a field initializer.
        //
        // Which encounter is loaded can depend on EditorPrefs (the Prototype
        // Editor's playtest hook), and Unity forbids reading those from a
        // MonoBehaviour constructor or field initializer — AddComponent runs
        // those outside the player loop. Worse, the exception aborts the whole
        // initializer list, so every readonly collection declared below this line
        // would be left null and the first real error would surface somewhere
        // unrelated. The caller resolves the name and hands it over instead.
        private string _encounterName = PrototypeBootstrap.EncounterName;

        private int _selectedUnitId = -1;
        private Coord? _hovered;
        private bool _enemyPhaseRunning;

        /// <summary>Launched from the Prototype Editor. Suppresses the map-switch keys.</summary>
        private bool _isEditorPlaytest;

        /// <summary>
        /// How much of the enemy side's reach to paint. Three states rather than
        /// a bool because "where can they WALK" and "where can they HIT" differ by
        /// the attack range, and for a range-2 archer that difference is the whole
        /// reason it is dangerous.
        /// </summary>
        private enum EnemyOverlay { Off, Threat, MoveAndThreat }

        private EnemyOverlay _enemyOverlay = EnemyOverlay.MoveAndThreat;
        private bool _showOwnRanges = true;

        /// <summary>
        /// The full debug overlay. OFF by default (專案負責人 2026-08-15) — the
        /// board is grayblock and the panels covered most of it. What it used to
        /// say goes to the Console instead. F3 brings it back.
        /// </summary>
        private bool _showHud;

        /// <summary>Controls / help sheet. ESC, with F1 as a spare — see HandleKeys.</summary>
        private bool _showHelp;

        /// <summary>
        /// Unit the info panel is pinned to, or -1 to follow the selection.
        /// Right-click pins; right-click empty ground unpins.
        /// </summary>
        private int _pinnedInspectId = -1;

        /// <summary>Last round a state block was printed, so it prints once per round.</summary>
        private int _lastReportedTurn = -1;

        private readonly List<string> _log = new List<string>();
        private ReachabilityMap _reach;
        private HashSet<Coord> _reachableCells = new HashSet<Coord>();
        private HashSet<Coord> _myReachCells = new HashSet<Coord>();
        private HashSet<Coord> _dangerCells = new HashSet<Coord>();
        private HashSet<Coord> _enemyMoveCells = new HashSet<Coord>();

        // Cached once per frame. OnGUI runs several times per frame, and these
        // queries each run a flood fill per enemy — recomputing them per event
        // would burn the whole frame on a HUD label.
        private Coord? _inspectedCell;
        private int _inspectedStaticExposure;
        private int _inspectedThreatCount;

        /// <summary>
        /// <paramref name="encounterName"/> is the resource this setup was built
        /// from, so R reloads what is actually on screen. Null keeps the default.
        /// </summary>
        public void Initialise(BattleSetup setup, string encounterName = null)
        {
            if (!string.IsNullOrEmpty(encounterName)) _encounterName = encounterName;

            // Read once, in Awake. Same reason _encounterName is not a field
            // initializer: this reaches EditorPrefs.
            _isEditorPlaytest = PrototypeBootstrap.IsEditorPlaytest;

            _setup = setup;
            _ai = setup.Ai;

            _view = gameObject.AddComponent<BattleView>();
            _view.Build(setup.State);

            StartBattle(setup.State);
        }

        private void StartBattle(BattleState initial)
        {
            CancelAction();
            _actionMessage = null;
            _log.Clear();
            _enemyPhaseRunning = false;

            ExecuteResult begin = BattleSimulator.Begin(initial);
            _state = begin.State;
            AppendLog(begin.Log);

            _selectedUnitId = -1;
            RecomputeOverlays();

            _lastReportedTurn = -1;
            ReportControls();
            ReportState();
        }

        private void Update()
        {
            if (_state == null) return;

            HandleHover();
            HandleKeys();
            HandleInspectClick();

            if (!_enemyPhaseRunning && _state.Outcome == BattleOutcome.InProgress
                && _state.CurrentFaction == Faction.Player)
            {
                HandleClick();
            }

            RefreshInspectedCell();
            _view.Refresh(_state, BuildOverlays());
        }

        /// <summary>
        /// Which cell sets to paint this frame.
        ///
        /// Hovering an enemy narrows the enemy overlays to THAT enemy. The union
        /// of six threat ranges covers most of the board and answers "is anywhere
        /// safe"; one enemy's range answers "what is this thing about to do to
        /// me", which is the question you have when you are looking at it.
        /// </summary>
        private BattleView.Overlays BuildOverlays()
        {
            BattleView.Overlays o = new BattleView.Overlays { Hovered = _hovered };

            UnitState selected = _selectedUnitId >= 0 ? _state.FindUnit(_selectedUnitId) : null;

            // Painted last and strongest by the view: while an action is aimed,
            // "what can I hit with THIS" is the only question on screen.
            if (_armedAction >= 0) o.ActionTargets = _targetCells;

            if (selected != null && selected.IsAlive && selected.Faction == Faction.Player && _showOwnRanges)
            {
                o.MyMove = _reachableCells;
                o.MyReach = _myReachCells;
            }

            if (selected != null && selected.IsAlive && selected.Faction == Faction.Enemy
                && _enemyOverlay != EnemyOverlay.Off)
            {
                // The enemy has already ended its previous phase while the
                // player is inspecting it. Show the selected enemy's next fresh
                // activation, not the union for the entire faction.
                o.EnemyThreat = BattleQueries.ThreatRange(_state, selected);
                if (_enemyOverlay == EnemyOverlay.MoveAndThreat)
                    o.EnemyMove = BattleQueries.MoveRange(_state, selected, fullBar: true);
            }

            return o;
        }

        private UnitState HoveredEnemy()
        {
            if (!_hovered.HasValue) return null;
            UnitState u = _state.UnitAt(_hovered.Value);
            return u != null && u.Faction == Faction.Enemy ? u : null;
        }

        private void RefreshInspectedCell()
        {
            if (!_hovered.HasValue) { _inspectedCell = null; return; }
            if (_inspectedCell.HasValue && _inspectedCell.Value == _hovered.Value) return;

            _inspectedCell = _hovered;
            _inspectedStaticExposure = BattleQueries.StaticExposure(_state.Map, _hovered.Value);
            _inspectedThreatCount = BattleQueries.EffectiveExposure(_state, _hovered.Value, Faction.Player);
        }

        // ----------------------------------------------------------------- input

        private void HandleHover()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) { _hovered = null; return; }

            Vector2 screen = mouse.position.ReadValue();
            _hovered = _view.ScreenToCell(Camera.main, new Vector3(screen.x, screen.y, 0f));
        }

        private void HandleKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.rKey.wasPressedThisFrame) { Load(_encounterName); return; }

            // ESC opens the help sheet, as asked for. F1 does the same thing
            // because ESC is not entirely ours: the editor's Game view grabs it to
            // release a locked cursor, and a standalone player uses it to leave
            // exclusive fullscreen. Neither applies here — nothing in this project
            // ever locks the cursor — but a help key that might silently do
            // nothing is worth one line of insurance.
            // ESC cancels an aimed action FIRST. Opening the help sheet on top of
            // a half-issued skill would be the one thing you never mean by it.
            if (kb.escapeKey.wasPressedThisFrame && _armedAction >= 0)
            {
                CancelAction();
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame || kb.f1Key.wasPressedThisFrame)
            {
                _showHelp = !_showHelp;
                return;
            }

            if (kb.f3Key.wasPressedThisFrame) _showHud = !_showHud;
            if (kb.f2Key.wasPressedThisFrame) { ReportState(force: true); return; }

            if (kb.tabKey.wasPressedThisFrame)
            {
                _enemyOverlay = _enemyOverlay == EnemyOverlay.Off ? EnemyOverlay.Threat
                              : _enemyOverlay == EnemyOverlay.Threat ? EnemyOverlay.MoveAndThreat
                              : EnemyOverlay.Off;
                RecomputeOverlays();
                ReportOverlayModes();
            }

            if (kb.zKey.wasPressedThisFrame)
            {
                _showOwnRanges = !_showOwnRanges;
                ReportOverlayModes();
            }

            // Flipping between layouts mid-session is the fastest way to feel what
            // the map structure is actually doing.
            // Digit0 is the tenth slot, not the zeroth — it sits to the right of 9
            // on the keyboard, so reading it as "10" is what the row already says.
            // Minus and Equals carry on along the same row for 11 and 12.
            // NOT offered during an editor playtest. The encounter under test
            // lives in the editor's document; loading a shipped map over it
            // discards what you came to look at and there is no key that brings
            // it back.
            if (!_isEditorPlaytest)
            {
                Key[] numbers = MapSelectKeys;
                for (int i = 0; i < numbers.Length && i < PrototypeBootstrap.SelectableEncounters.Length; i++)
                {
                    if (!kb[numbers[i]].wasPressedThisFrame) continue;
                    Load(PrototypeBootstrap.SelectableEncounters[i]);
                    return;
                }
            }

            if (_enemyPhaseRunning || _state.Outcome != BattleOutcome.InProgress) return;
            if (_state.CurrentFaction != Faction.Player) return;

            // Every action now comes from the same list the action bar draws, so
            // a shortcut and a button can never mean different things — and a
            // unit that gains a skill in units.txt gains both at once.
            UnitState selected = SelectedUnit();
            if (selected != null)
            {
                List<UnitAction> actions = ActionsFor(selected);
                for (int i = 0; i < actions.Count; i++)
                {
                    Key key = BattleKeys.KeyFor(actions[i].ShortcutLabel);
                    if (key == Key.None || !kb[key].wasPressedThisFrame) continue;
                    ChooseAction(i);
                    return;
                }
            }

            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                EndPlayerTurn();
        }

        // ------------------------------------------------------------- actions

        /// <summary>
        /// Index into the selected unit's action list that is waiting for a
        /// target, or -1. Only ever set for ActionTarget.Enemy actions.
        /// </summary>
        private int _armedAction = -1;

        private List<UnitAction> _actionCache;
        private string _actionCacheFor;

        /// <summary>Cells holding a legal target for the armed action. Painted by the view.</summary>
        private readonly HashSet<Coord> _targetCells = new HashSet<Coord>();

        /// <summary>Last rejection, shown on the bar so a refusal is never silent.</summary>
        private string _actionMessage;

        private UnitState SelectedUnit()
        {
            if (_selectedUnitId < 0) return null;
            UnitState u = _state.FindUnit(_selectedUnitId);
            return u != null && u.IsAlive && u.Faction == Faction.Player ? u : null;
        }

        /// <summary>Rebuilt only when the unit TYPE changes — the list is a function of UnitDef.</summary>
        private List<UnitAction> ActionsFor(UnitState unit)
        {
            if (_actionCache != null && _actionCacheFor == unit.Def.Id) return _actionCache;
            _actionCache = BattleActions.For(unit.Def);
            _actionCacheFor = unit.Def.Id;
            return _actionCache;
        }

        /// <summary>
        /// Self-targeted actions fire immediately; targeted ones arm and wait for
        /// a click on the board.
        ///
        /// Arming rather than "press the key while hovering the target" is the
        /// whole point of the change: with eight actions and several of them
        /// aimed, a modifier-free chord per skill stops scaling, and there is
        /// nothing on screen that tells you what is aimable or how far it reaches.
        /// </summary>
        private void ChooseAction(int index)
        {
            UnitState actor = SelectedUnit();
            if (actor == null) return;

            List<UnitAction> actions = ActionsFor(actor);
            if (index < 0 || index >= actions.Count) return;

            UnitAction action = actions[index];
            _actionMessage = null;

            if (_armedAction == index) { CancelAction(); return; }

            if (action.Target == ActionTarget.Self)
            {
                _armedAction = -1;
                Submit(action.Build(actor.Id, -1));
                return;
            }

            _armedAction = index;
            RecomputeTargets();

            if (_targetCells.Count == 0)
            {
                _actionMessage = action.Label + "：射程 " + action.Range + " 內沒有敵人";
                _armedAction = -1;
                _targetCells.Clear();
            }
        }

        private void CancelAction()
        {
            _armedAction = -1;
            _targetCells.Clear();
        }

        /// <summary>
        /// Which enemies the armed action could be pointed at.
        ///
        /// Range only. Whether the command actually resolves — a push into a wall,
        /// a slow that is already applied — stays with BattleSimulator, and the
        /// rejection it gives back is shown on the bar. The UI must not grow its
        /// own copy of those rules, or the two will disagree.
        /// </summary>
        private void RecomputeTargets()
        {
            _targetCells.Clear();
            if (_armedAction < 0) return;

            UnitState actor = SelectedUnit();
            if (actor == null) return;

            UnitAction action = ActionsFor(actor)[_armedAction];

            if (action.Target == ActionTarget.SelfConfirm)
            {
                int radius = actor.Def.PurifyRadius;
                for (int y = -radius; y <= radius; y++)
                    for (int x = -(radius - Math.Abs(y)); x <= radius - Math.Abs(y); x++)
                    {
                        Coord c = new Coord(actor.Position.X + x, actor.Position.Y + y);
                        if (_state.Map.Contains(c)) _targetCells.Add(c);
                    }
                return;
            }

            for (int i = 0; i < _state.Units.Count; i++)
            {
                UnitState u = _state.Units[i];
                if (!u.IsAlive || u.Faction != Faction.Enemy) continue;
                if (_state.Map.Topology.Distance(actor.Position, u.Position) > action.Range) continue;
                _targetCells.Add(u.Position);
            }
        }

        /// <summary>
        /// Right-click pins the info panel to any unit, friend or foe, without
        /// doing anything to it.
        ///
        /// It has to be a separate button from left-click: left-clicking an enemy
        /// attacks it, so "click it to read its stats" and "click it to hit it"
        /// cannot be the same gesture. Right-click also works during the enemy
        /// phase and after the battle ends, which is when you most want to look.
        /// </summary>
        private void HandleInspectClick()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.wasPressedThisFrame) return;

            UnitState u = _hovered.HasValue ? _state.UnitAt(_hovered.Value) : null;
            _pinnedInspectId = u != null ? u.Id : -1;
        }


        private void HandleClick()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            // IMGUI eats clicks for its own controls, but the new Input System
            // does not know that — without this the same click both presses a
            // button and orders a move on the cell behind it.
            if (PointerOverActionBar(mouse)) return;

            if (!_hovered.HasValue) return;

            Coord cell = _hovered.Value;
            UnitState occupant = _state.UnitAt(cell);

            UnitState selected = SelectedUnit();

            // An armed action owns the next click on the board. Clicking anywhere
            // that is not a legal target cancels it, which is what every strategy
            // game does and means you never get stuck in a targeting mode.
            if (_armedAction >= 0)
            {
                List<UnitAction> actions = ActionsFor(selected);
                UnitAction action = actions[_armedAction];

                bool validEnemy = action.Target == ActionTarget.Enemy
                               && occupant != null && occupant.Faction == Faction.Enemy
                               && _targetCells.Contains(cell);
                bool validSelfConfirm = action.Target == ActionTarget.SelfConfirm
                                     && _targetCells.Contains(cell);
                if (validEnemy || validSelfConfirm)
                {
                    ICommand command = action.Build(selected.Id, validEnemy ? occupant.Id : selected.Id);
                    CancelAction();
                    Submit(command);
                }
                else
                {
                    CancelAction();
                    _actionMessage = action.Label + " 取消了";
                }
                return;
            }

            if (occupant != null)
            {
                CancelAction();
                _selectedUnitId = occupant.Id;
                RecomputeOverlays();
                return;
            }

            if (selected != null && _reach != null && _reach.CanReach(cell))
            {
                Coord[] path = _reach.PathTo(cell);
                if (path != null && path.Length > 0) Submit(new MoveCommand(selected.Id, path));
            }
        }

        /// <summary>
        /// Clicking an enemy that is one step out of reach used to do nothing, so
        /// the player had to click an empty cell first and then the enemy. That
        /// shuffle is not a decision — it is a second click for a move you had no
        /// choice about.
        ///
        /// So: in range, attack. Out of range, step to the SAFEST cell that can
        /// reach it and attack from there. Safest, not nearest, because this is a
        /// game about where you end up standing — auto-picking an exposed cell
        /// would be making the actual decision for the player.
        ///
        /// No rule changes: this issues the same two Commands the player would.
        /// </summary>
        private void AttackOrCloseIn(UnitState attacker, UnitState target)
        {
            if (_state.Map.Topology.Distance(attacker.Position, target.Position) <= attacker.Def.AttackRange)
            {
                Submit(new AttackCommand(attacker.Id, target.Id));
                return;
            }

            if (_reach == null) return;

            Coord best = attacker.Position;
            bool found = false;
            int bestExposure = int.MaxValue;
            int bestCost = int.MaxValue;

            for (int i = 0; i < _reach.ReachableCells.Count; i++)
            {
                Coord cell = _reach.ReachableCells[i];
                if (cell == attacker.Position) continue;
                if (_state.Map.Topology.Distance(cell, target.Position) > attacker.Def.AttackRange) continue;

                // Enough AP left to actually swing once we get there.
                int cost = _reach.CostTo(cell);
                if (attacker.Ap - cost < attacker.Def.AttackApCost) continue;

                int exposure = BattleQueries.EffectiveExposure(_state, cell, Faction.Player);
                if (exposure > bestExposure || (exposure == bestExposure && cost >= bestCost)) continue;

                bestExposure = exposure;
                bestCost = cost;
                best = cell;
                found = true;
            }

            if (!found)
            {
                _log.Add("<no route> cannot reach u" + target.Id + " with enough AP left to attack");
                return;
            }

            Coord[] path = _reach.PathTo(best);
            if (path == null || path.Length == 0) return;

            _log.Add("(auto) step to " + best + " — threatened by " + bestExposure + " — then attack");
            Submit(new MoveCommand(attacker.Id, path));

            UnitState moved = _state.FindUnit(attacker.Id);
            if (moved != null && moved.IsAlive && _state.Outcome == BattleOutcome.InProgress)
                Submit(new AttackCommand(moved.Id, target.Id));
        }

        // -------------------------------------------------------------- commands

        private void Submit(ICommand command)
        {
            ExecuteResult result = BattleSimulator.Execute(_state, command);

            if (!result.Ok)
            {
                // Rejections go to the Console unconditionally. With the overlay
                // off, a silently refused command is indistinguishable from a
                // dead keybind, and that is the worst thing a prototype can do to
                // someone still learning what the rules are.
                _log.Add("<rejected> " + command.Describe() + " : " + result.RejectReason);
                Debug.Log(ConsoleTag + "rejected: " + command.Describe() + "  —  " + result.RejectReason);

                // Also onto the bar. The console is behind the game view during a
                // playtest, so a refusal that only lands there reads as a dead
                // button — the exact failure the comment above warns about.
                _actionMessage = result.RejectReason;
                return;
            }

            _actionMessage = null;
            _state = result.State;
            AppendLog(result.Log);
            RecomputeOverlays();

            if (_state.Outcome != BattleOutcome.InProgress) ReportOutcome();
        }

        private void EndPlayerTurn()
        {
            CancelAction();
            ExecuteResult result = BattleSimulator.Execute(_state, new EndTurnCommand(Faction.Player));
            if (!result.Ok)
            {
                _log.Add("<rejected> end turn : " + result.RejectReason);
                Debug.Log(ConsoleTag + "rejected: end turn  —  " + result.RejectReason);
                return;
            }

            _state = result.State;
            AppendLog(result.Log);
            RecomputeOverlays();

            if (_state.Outcome == BattleOutcome.InProgress)
                StartCoroutine(RunEnemyPhase());
        }

        /// <summary>
        /// Steps the enemy phase one command at a time so it can actually be watched.
        /// EnemyAi.RunFactionTurn does the same thing in one call for headless runs.
        /// </summary>
        private IEnumerator RunEnemyPhase()
        {
            _enemyPhaseRunning = true;

            List<int> enemyIds = new List<int>();
            for (int i = 0; i < _state.Units.Count; i++)
                if (_state.Units[i].Faction == Faction.Enemy) enemyIds.Add(_state.Units[i].Id);

            for (int i = 0; i < enemyIds.Count; i++)
            {
                for (int guard = 0; guard < MaxEnemyCommandsPerUnit; guard++)
                {
                    if (_state.Outcome != BattleOutcome.InProgress) break;

                    UnitState unit = _state.FindUnit(enemyIds[i]);
                    ICommand cmd = _ai.DecideNext(_state, unit);
                    if (cmd == null) break;

                    ExecuteResult r = BattleSimulator.Execute(_state, cmd);
                    if (!r.Ok)
                    {
                        _log.Add("<ai rejected> " + cmd.Describe() + " : " + r.RejectReason);
                        break;
                    }

                    _state = r.State;
                    AppendLog(r.Log);
                    RecomputeOverlays();

                    bool waited = cmd is WaitCommand;
                    if (!waited) yield return new WaitForSeconds(EnemyStepSeconds);
                    if (waited) break;
                }

                if (_state.Outcome != BattleOutcome.InProgress) break;
            }

            if (_state.Outcome == BattleOutcome.InProgress)
            {
                ExecuteResult end = BattleSimulator.Execute(_state, new EndTurnCommand(Faction.Enemy));
                if (end.Ok)
                {
                    _state = end.State;
                    AppendLog(end.Log);
                    _selectedUnitId = -1;
                    RecomputeOverlays();
                }
            }

            _enemyPhaseRunning = false;

            // Printed here rather than on the player's first input, so the block
            // reflects the board as the enemy phase left it — which is what the
            // next decision is actually made against.
            if (_state.Outcome == BattleOutcome.InProgress) ReportState();
            else ReportOutcome();
        }

        /// <summary>Loads (or reloads) an encounter and rebuilds the board.</summary>
        private void Load(string encounterName)
        {
            BattleSetup fresh;
            string error;
            if (!PrototypeBootstrap.TryLoadBattle(encounterName, out fresh, out error))
            {
                Debug.LogError("[Ediki] Could not load '" + encounterName + "': " + error);
                return;
            }

            StopAllCoroutines();
            _enemyPhaseRunning = false;

            _encounterName = encounterName;
            _setup = fresh;
            _ai = fresh.Ai;

            _view.Build(fresh.State);
            StartBattle(fresh.State);
        }

        // ------------------------------------------------------------- overlays

        private void RecomputeOverlays()
        {
            _reachableCells.Clear();
            _myReachCells.Clear();
            _reach = null;

            UnitState selected = _selectedUnitId >= 0 ? _state.FindUnit(_selectedUnitId) : null;
            if (selected != null && selected.IsAlive && _state.CurrentFaction == Faction.Player)
            {
                _reach = MovementCalculator.ComputeFor(_state, selected);
                for (int i = 0; i < _reach.ReachableCells.Count; i++)
                    _reachableCells.Add(_reach.ReachableCells[i]);

                // What it could hit this turn, including after moving — the ring
                // beyond the movement area. Uses the same query the danger zone
                // uses for enemies, so both sides are measured the same way.
                _myReachCells = BattleQueries.CurrentThreatRange(_state, selected);
            }

            _dangerCells = BattleQueries.DangerZone(_state, Faction.Player);
            _enemyMoveCells = _enemyOverlay == EnemyOverlay.MoveAndThreat
                ? BattleQueries.EnemyMoveZone(_state, Faction.Player)
                : new HashSet<Coord>();

            _inspectedCell = null;   // state changed: the cached hover readout is stale

            // Enemies move and die, so a target set computed a command ago is not
            // one to aim with.
            RecomputeTargets();
        }

        private int FirstLivingPlayerUnitId()
        {
            foreach (UnitState u in _state.LivingUnitsOf(Faction.Player)) return u.Id;
            return -1;
        }

        private void AppendLog(EffectLog log)
        {
            for (int i = 0; i < log.Count; i++) _log.Add(log[i].Describe());
            const int keep = 200;
            if (_log.Count > keep) _log.RemoveRange(0, _log.Count - keep);
        }

        // -------------------------------------------------------------- console
        //
        // With the overlay off, the Console IS the interface. Three things print:
        //   on load       the control scheme and the unit legend
        //   each round    a state block, once, at the start of the player phase
        //   on rejection  why the command was refused
        //
        // Everything here is presentation only — it reads state and formats it,
        // and calls nothing that could mutate a battle (A7).

        private const string ConsoleTag = "[Ediki] ";

        private void ReportControls()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(ConsoleTag + "=== " + _setup.Encounter.DisplayName + " ===");
            sb.AppendLine("objective : " + _state.Objective.Describe());
            sb.AppendLine();
            sb.AppendLine("        >>> press ESC (or F1) at any time for the controls sheet on screen <<<");
            sb.AppendLine();
            sb.AppendLine("MOUSE   left  your unit = select   a cell = move   an enemy = attack");
            sb.AppendLine("        RIGHT any unit  = inspect it (stats, AP, skill costs). No action taken.");
            sb.AppendLine("        right-click empty ground to unpin. Hovering an enemy narrows the");
            sb.AppendLine("        red/amber overlay to just that one.");
            sb.AppendLine("        moving costs AP *and* MOVE. Both are per turn.");
            sb.AppendLine();
            sb.AppendLine("KEYS    ESC/F1 controls sheet      R    restart this map");
            sb.AppendLine("        space  end turn            F3   full debug panel");
            sb.AppendLine("        The ACTION BAR at the bottom of the screen lists everything the");
            sb.AppendLine("        selected unit can do, built from its own data. Aimed skills arm");
            sb.AppendLine("        first — click the button, then click a highlighted enemy.");
            sb.AppendLine("        TAB    cycle THEIR overlay: threat -> +movement -> off");
            sb.AppendLine("        Z      toggle YOUR overlay");
            sb.AppendLine("        F2     reprint the state block");
            sb.AppendLine("        1-9 0 - = [ ]   switch map");
            sb.AppendLine();
            sb.AppendLine("RULES   AP and MOVE are two SEPARATE budgets, both per turn.");
            sb.AppendLine("          AP   how many things you can do. Cap 10, +8 at the start of your");
            sb.AppendLine("               turn, and UNSPENT AP CARRIES OVER. Banking 2 this turn means");
            sb.AppendLine("               10 next turn — enough for two attacks AND a skill.");
            sb.AppendLine("          MOVE how far you can walk. Runs out independently of AP.");
            sb.AppendLine("        Terrain costs AP to enter: road 1, forest 2. Going round can be");
            sb.AppendLine("          cheaper than going through.");
            sb.AppendLine("        Damage = max(1, attacker ATK - defender DEF). No dice, no misses.");
            sb.AppendLine("          So low ATK into high DEF chips for 1 — check the hit counts.");
            sb.AppendLine("        Guard halves incoming damage until your next turn.");
            sb.AppendLine("        Units block each other. A one-wide gap passes one unit per turn.");
            sb.AppendLine("        Enemies wake when you enter their reach, and then they COME TO YOU");
            sb.AppendLine("          — except cylinders, which never move at all.");
            sb.AppendLine("        Killing everything always wins, whatever the stated objective.");
            sb.AppendLine();
            sb.AppendLine("BOARD   cube = melee   capsule = ranged   cylinder = CANNOT MOVE (it will");
            sb.AppendLine("        never come to you, so engaging it is entirely your choice)");
            sb.AppendLine("        bigger footprint = more HP.  Taller = the objective target.");
            sb.AppendLine("        green->violet = yours, red->amber = theirs, dimmed = not woken yet.");
            sb.AppendLine("        yellow tint = guarding or taunting, pale blue tint = slowed.");
            sb.AppendLine();
            sb.AppendLine("CELLS   BLUE    you can stand here this turn");
            sb.AppendLine("        CYAN    you can hit here this turn, after moving");
            sb.AppendLine("        RED     they can hit here");
            sb.AppendLine("        AMBER   they can walk here but not hit it (TAB twice to show)");
            sb.AppendLine("        MAGENTA both — you can stand here AND be hit for it.");
            sb.AppendLine("                This is the cell every decision is actually about.");
            sb.AppendLine("        Hover an enemy to narrow the red/amber to just that one.");
            sb.AppendLine();
            sb.Append(RosterLegend());
            Debug.Log(sb.ToString());
        }

        private string RosterLegend()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("ROSTER");
            for (int i = 0; i < _state.Units.Count; i++)
            {
                UnitState u = _state.Units[i];
                sb.Append(u.Faction == Faction.Player ? "  YOU " : "  FOE ")
                  .Append("u").Append(u.Id).Append(' ')
                  .Append(u.Def.DisplayName.PadRight(15))
                  .Append(BattleView.DescribeVisual(u.Def, u.Faction))
                  .Append("  HP ").Append(u.Def.MaxHp.ToString().PadLeft(3))
                  .Append("  ATK ").Append(u.Def.Atk.ToString().PadLeft(3))
                  .Append("  DEF ").Append(u.Def.Def.ToString().PadLeft(2))
                  .Append("  MOVE ").Append(u.Def.Move)
                  .Append("  RANGE ").Append(u.Def.AttackRange)
                  .Append(Skills(u.Def))
                  .AppendLine();
            }
            return sb.ToString();
        }

        private static string Skills(UnitDef def)
        {
            string s = "";
            if (def.CanTaunt) s += "  [T]aunt " + def.TauntApCost + "ap r" + def.TauntRadius;
            if (def.CanSlow) s += "  slow[F] " + def.SlowApCost + "ap r" + def.SlowRange;
            if (def.CanPush) s += "  push[V] " + def.PushApCost + "ap";
            if (def.ImmuneToPush) s += "  (immune to push)";
            return s;
        }

        /// <summary>
        /// The state block. Prints once per round unless forced (F2).
        ///
        /// The one number worth more than any other to someone new to the genre is
        /// HITS — how many swings this unit needs to remove that enemy. It is the
        /// difference between "I can clear it this turn" and "I am starting
        /// something I cannot finish", and it is not visible anywhere else.
        /// </summary>
        private void ReportState(bool force = false)
        {
            if (_state == null) return;
            if (!force && _state.TurnIndex == _lastReportedTurn) return;
            _lastReportedTurn = _state.TurnIndex;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(ConsoleTag).Append("--- turn ").Append(_state.TurnIndex)
              .Append("  ").Append(_state.CurrentFaction)
              .Append("  |  enemies left ").Append(_state.CountLiving(Faction.Enemy));
            if (_state.Objective.HasTurnLimit)
                sb.Append("  |  ").Append(_state.Objective.TurnLimit - _state.TurnIndex + 1).Append(" turns left");
            sb.AppendLine();

            UnitState selected = _selectedUnitId >= 0 ? _state.FindUnit(_selectedUnitId) : null;

            foreach (UnitState u in _state.LivingUnitsOf(Faction.Player))
            {
                int moveLeft = u.Def.Move - u.MoveUsedThisTurn;
                if (moveLeft < 0) moveLeft = 0;

                sb.Append(u.Id == _selectedUnitId ? "  > " : "    ")
                  .Append("u").Append(u.Id).Append(' ').Append(u.Def.DisplayName.PadRight(15))
                  .Append("HP ").Append((u.Hp + "/" + u.Def.MaxHp).PadRight(9))
                  .Append("AP ").Append(u.Ap.ToString().PadLeft(2))
                  .Append("  MOVE ").Append(moveLeft)
                  .Append("  at ").Append(u.Position.ToString().PadRight(8))
                  .Append("threatened by ")
                  .Append(BattleQueries.EffectiveExposure(_state, u.Position, Faction.Player))
                  .Append(StatusFlags(u))
                  .AppendLine();
            }

            foreach (UnitState e in _state.LivingUnitsOf(Faction.Enemy))
            {
                sb.Append("    e").Append(e.Id).Append(' ').Append(e.Def.DisplayName.PadRight(15))
                  .Append("HP ").Append((e.Hp + "/" + e.Def.MaxHp).PadRight(9))
                  .Append("at ").Append(e.Position.ToString().PadRight(8))
                  .Append("range ").Append(e.Def.AttackRange)
                  .Append(e.Def.Move == 0 ? " STATIC" : "")
                  .Append(CanHitSomeone(e) ? "  <- can hit you now" : "")
                  .Append(selected != null ? "   " + HitsNeeded(selected, e) + " hits from u" + selected.Id : "")
                  .Append(StatusFlags(e))
                  .AppendLine();
            }

            Debug.Log(sb.ToString());
        }

        private string StatusFlags(UnitState u)
        {
            string s = "";
            if (u.IsGuarding) s += "  GUARD";
            if (_state.IsSlowed(u)) s += "  SLOWED";
            if (_state.IsTaunting(u)) s += "  TAUNTING";
            if (u.IsObjectiveTarget) s += "  <<TARGET>>";
            if (u.HasEndedTurn) s += "  done";
            return s;
        }

        private bool CanHitSomeone(UnitState enemy)
        {
            foreach (UnitState p in _state.LivingUnitsOf(Faction.Player))
                if (_state.Map.Topology.Distance(enemy.Position, p.Position) <= enemy.Def.AttackRange) return true;
            return false;
        }

        private int HitsNeeded(UnitState attacker, UnitState target)
        {
            int perHit = BattleRules.ComputeDamage(attacker.Def.AtkOnRound(_state.TurnIndex),
                                                   target.Def.Def, target.IsGuarding, _state.Rules.Damage);
            if (perHit < 1) perHit = 1;
            return (target.Hp + perHit - 1) / perHit;
        }

        private void ReportOverlayModes()
        {
            Debug.Log(ConsoleTag + "overlay  —  yours: " + (_showOwnRanges ? "ON" : "off")
                      + "   theirs: " + _enemyOverlay
                      + "\n  BLUE you can stand here   CYAN you can hit here (after moving)"
                      + "\n  RED they can hit here     AMBER they can walk here but not hit"
                      + "\n  MAGENTA both — you can stand here AND be hit for it"
                      + "\n  hover an enemy to see only THAT enemy's ranges.");
        }

        private void ReportOutcome()
        {
            Debug.Log(ConsoleTag + (_state.Outcome == BattleOutcome.Victory ? "*** VICTORY ***" : "*** DEFEAT ***")
                      + "   turn " + _state.TurnIndex
                      + "   enemies left " + _state.CountLiving(Faction.Enemy)
                      + "   — press R to restart");
        }

        // ----------------------------------------------------------- ESC / panel

        /// <summary>Full-screen controls sheet. ESC (or F1).</summary>
        private void DrawHelpPanel()
        {
            float w = Mathf.Min(760f, Screen.width - 40f);
            float h = Mathf.Min(560f, Screen.height - 40f);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            GUILayout.BeginArea(new Rect(x + 18, y + 14, w - 36, h - 28));

            GUILayout.Label("<b>操作方式</b>        ESC 或 F1 關閉");
            GUILayout.Space(6);

            GUILayout.Label("<b>滑鼠</b>");
            GUILayout.Label("  左鍵自己人 = 選取      左鍵空格 = 移動（同時扣 AP 與 MOVE）");
            GUILayout.Label("  左鍵敵人   = 攻擊      <b>右鍵任何單位 = 查看資料（不會動手）</b>");
            GUILayout.Label("  右鍵空地   = 取消查看  滑鼠停在敵人上 = 只顯示那一隻的範圍");
            GUILayout.Space(6);

            GUILayout.Label("<b>行動</b>");
            GUILayout.Label("  <b>畫面下方的行動列就是全部能做的事</b>，會依照選中單位的資料自動變化。");
            GUILayout.Label("  需要指定目標的技能：<b>先點按鈕</b>（或按快速鍵），可以打的敵人會亮成金色，");
            GUILayout.Label("  <b>再點那個敵人</b>就施放。點別的地方或按 Esc 取消。");
            GUILayout.Label("  按鈕上會寫 AP 花費與射程；AP 不夠會變灰。滑鼠停在按鈕上有說明。");
            GUILayout.Label("  空白鍵  結束回合");
            GUILayout.Space(6);

            GUILayout.Label("<b>顯示</b>");
            GUILayout.Label("  TAB 敵方範圍：威脅 → ＋移動 → 關      Z 我方範圍開關");
            GUILayout.Label("  F2 把盤面重印到 Console      F3 完整除錯面板      R 重開這張圖");
            GUILayout.Label("  1-9 0 - = [ ]  換地圖");
            GUILayout.Space(6);

            GUILayout.Label("<b>格子顏色</b>");
            GUILayout.Label("  <b>藍</b> 你站得上去    <b>青</b> 你打得到（含先移動）");
            GUILayout.Label("  <b>紅</b> 他們打得到    <b>琥珀</b> 他們走得到但打不到");
            GUILayout.Label("  <b>洋紅</b> 兩者皆是 —— 這格就是每個決定真正在講的東西");
            GUILayout.Space(6);

            GUILayout.Label("<b>棋子</b>");
            GUILayout.Label("  方塊 = 近戰    膠囊 = 遠程    <b>圓柱 = 永遠不會移動</b>");
            GUILayout.Label("  底面積越大 HP 越多    特別高 = 目標    暗色 = 還沒發現你");
            GUILayout.Space(6);

            GUILayout.Label("<b>兩個一定會搞混的</b>");
            GUILayout.Label("  1. AP 和 MOVE 是兩個獨立預算。<b>沒用完的 AP 會留到下回合</b>（上限 10、每回合 +8）");
            GUILayout.Label("  2. 傷害 = max(1, 攻方 ATK − 守方 DEF)，必中。低攻打高防只有保底 1 傷");

            GUILayout.EndArea();
        }

        /// <summary>Which unit the info panel is about.</summary>
        private UnitState InspectTarget()
        {
            if (_pinnedInspectId >= 0)
            {
                UnitState pinned = _state.FindUnit(_pinnedInspectId);
                if (pinned != null && pinned.IsAlive) return pinned;
                _pinnedInspectId = -1;
            }
            return _selectedUnitId >= 0 ? _state.FindUnit(_selectedUnitId) : null;
        }

        /// <summary>
        /// The unit panel: what this unit has left, and what everything costs.
        ///
        /// Costs sit next to the remaining AP on purpose. "10 AP" means nothing on
        /// its own; "10 AP, and an attack is 4" is a plan.
        /// </summary>
        private void DrawUnitPanel()
        {
            UnitState u = InspectTarget();
            if (u == null || !u.IsAlive) return;

            bool mine = u.Faction == Faction.Player;
            int moveLeft = Mathf.Max(0, u.Def.Move - u.MoveUsedThisTurn);

            const float w = 330f;
            float h = mine ? 250f : 150f;
            float x = 12f;
            float y = Screen.height - h - 12f;

            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            GUILayout.BeginArea(new Rect(x + 12, y + 10, w - 24, h - 20));

            GUILayout.Label("<b>" + u.Def.DisplayName + "</b>  u" + u.Id
                            + (mine ? "" : "   (敵方)")
                            + (_pinnedInspectId == u.Id ? "   [已釘選 · 右鍵空地取消]" : ""));
            GUILayout.Label("HP <b>" + u.Hp + "</b> / " + u.Def.MaxHp
                            + "     ATK " + u.Def.Atk + "   DEF " + u.Def.Def
                            + "   射程 " + u.Def.AttackRange);

            if (mine)
            {
                GUILayout.Label("剩餘 AP <b>" + u.Ap + "</b> / " + u.Def.MaxAp
                                + "（每回合 +" + u.Def.ApRegen + "，<b>沒用完會留著</b>）");
                GUILayout.Label("剩餘 MOVE <b>" + moveLeft + "</b> / " + u.Def.Move
                                + "     這格被 " + BattleQueries.EffectiveExposure(_state, u.Position, Faction.Player)
                                + " 隻打得到");
                GUILayout.Space(4);
                GUILayout.Label("<b>可用行動</b>（括號 = 現在按得動嗎）");
                foreach (string line in ActionLines(u)) GUILayout.Label(line);
            }
            else
            {
                GUILayout.Label("MOVE " + u.Def.Move + (u.Def.Move == 0 ? "  <b>（永遠不會移動）</b>" : "")
                                + "     每回合可攻擊 "
                                + (u.Def.AttackApCost <= 0 ? 0 : u.Def.ApRegen / u.Def.AttackApCost) + " 次");

                UnitState me = _selectedUnitId >= 0 ? _state.FindUnit(_selectedUnitId) : null;
                if (me != null && me.IsAlive)
                {
                    GUILayout.Label("u" + me.Id + " 要 <b>" + HitsNeeded(me, u) + "</b> 刀才殺得掉它");
                    GUILayout.Label("它每刀對 " + me.Def.DisplayName + " 造成 <b>"
                                    + BattleRules.ComputeDamage(u.Def.AtkOnRound(_state.TurnIndex),
                                                                me.Def.Def, false, _state.Rules.Damage)
                                    + "</b> 傷害");
                }
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// One line per action this unit actually has, with its cost and what it
        /// does. Generated from UnitDef rather than written out, so the numbers
        /// shown are the numbers the simulator will use — a hand-written tooltip
        /// is one balance change away from lying.
        /// </summary>
        private List<string> ActionLines(UnitState u)
        {
            // Built from the SAME list the bar and the shortcuts read.
            //
            // This used to be a hand-written parallel copy, which meant a unit
            // could gain a skill and appear on the bar while this panel silently
            // went on describing the old kit — the exact drift the comment above
            // warns about, one level up.
            List<string> lines = new List<string>();

            foreach (UnitAction action in BattleActions.For(u.Def))
            {
                if (action.Label == "待機") continue;   // not a skill; the bar shows it

                string cost = action.ApCost > 0 ? action.ApCost + " AP" : "免費";
                string range = action.Target == ActionTarget.Enemy ? "  射程 " + action.Range : "";

                lines.Add(Afford(u, action.ApCost) + action.Label + " [" + action.ShortcutLabel + "]  "
                          + cost + range + " —— " + action.Hint);
            }

            if (u.Def.ImmuneToPush) lines.Add("    免疫擊退");
            return lines;
        }

        private static string Afford(UnitState u, int cost) => u.Ap >= cost ? "  ✔ " : "  ✘ ";

        // ----------------------------------------------------------- nameplates
        //
        // Not decoration. The board used to give every unit type its own hue
        // inside its faction's band, so colour answered both "whose is it" and
        // "which one is it". That is one channel doing two jobs, and it stops
        // working the moment the roster outgrows a handful of types.
        //
        // Colour now means side only — the same grammar the editor draws with —
        // and identity moved here, to text, which is the only channel that keeps
        // working at twenty unit types.

        private GUIStyle _plateTag;
        private GUIStyle _plateName;

        private void DrawNameplates()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (_plateTag == null)
            {
                _plateTag = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontSize = 11, richText = true };
                _plateName = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontSize = 10, richText = false };
            }

            int player = 0;
            int enemy = 0;

            for (int i = 0; i < _state.Units.Count; i++)
            {
                UnitState u = _state.Units[i];

                // Numbered in id order, which is spawn order — the same P1 / E3
                // the editor shows, so a unit has one name across both windows.
                string tag = u.Faction == Faction.Player ? "P" + (++player) : "E" + (++enemy);
                if (!u.IsAlive) continue;

                Vector3 screen = cam.WorldToScreenPoint(BattleView.NameplateAnchor(u));
                if (screen.z <= 0f) continue;   // behind the camera

                float x = screen.x;
                float y = Screen.height - screen.y;

                Rect plate = new Rect(x - 54f, y - 26f, 108f, 26f);
                if (plate.yMax < 0f || plate.y > Screen.height) continue;

                Color previous = GUI.color;

                GUI.color = new Color(0.06f, 0.06f, 0.08f, u.Id == _selectedUnitId ? 0.85f : 0.62f);
                GUI.DrawTexture(plate, Texture2D.whiteTexture);

                GUI.color = PrototypeVisuals.BodyColor(u.Faction, u.IsObjectiveTarget || u.MustSurvive);
                GUI.Label(new Rect(plate.x, plate.y, plate.width, 13f),
                          tag + (u.Id == _selectedUnitId ? "  ◀" : ""), _plateTag);

                GUI.color = new Color(0.92f, 0.92f, 0.94f, u.IsActivated || u.Faction == Faction.Player ? 1f : 0.6f);
                GUI.Label(new Rect(plate.x, plate.y + 12f, plate.width, 13f), u.Def.DisplayName, _plateName);

                GUI.color = previous;
            }
        }

        // ----------------------------------------------------------- action bar
        //
        // The primary way to play, and the reason it exists rather than more
        // keybinds: the roster is going to grow skills, several of them are
        // aimed, and a chord-per-skill scheme has no room left and nothing on
        // screen that says what is aimable or how far it reaches.
        //
        // Drawn from the same list the shortcuts read, so the two cannot drift.

        private const float BarButtonWidth = 96f;
        private const float BarButtonHeight = 46f;
        private const float BarPadding = 8f;
        private const float BarBottomMargin = 12f;

        private static readonly Color ArmedTint = new Color(1f, 0.82f, 0.35f);
        private static readonly Color UnaffordableTint = new Color(0.55f, 0.55f, 0.58f);

        /// <summary>Where the bar sits. A pure function of the screen and the action count.</summary>
        private Rect ActionBarRect(int actionCount)
        {
            float width = actionCount * BarButtonWidth + (actionCount + 1) * BarPadding;
            float height = BarButtonHeight + BarPadding * 2f;
            return new Rect((Screen.width - width) * 0.5f,
                            Screen.height - height - BarBottomMargin,
                            width, height);
        }

        /// <summary>
        /// Input System coordinates have y = 0 at the BOTTOM; GUI rects have it
        /// at the top. Getting this backwards makes the guard protect the wrong
        /// half of the screen, which is worse than no guard at all.
        /// </summary>
        private bool PointerOverActionBar(Mouse mouse)
        {
            UnitState actor = SelectedUnit();
            if (actor == null) return false;
            if (_state.CurrentFaction != Faction.Player || _enemyPhaseRunning) return false;

            Vector2 screen = mouse.position.ReadValue();
            Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);

            return ActionBarRect(ActionsFor(actor).Count).Contains(gui);
        }

        private void DrawActionBar()
        {
            if (_state.Outcome != BattleOutcome.InProgress) return;

            UnitState actor = SelectedUnit();
            if (actor == null) return;

            List<UnitAction> actions = ActionsFor(actor);
            Rect bar = ActionBarRect(actions.Count);

            bool actorCanAct = _state.CurrentFaction == Faction.Player
                            && !_enemyPhaseRunning
                            && !actor.HasEndedTurn;

            GUI.Box(bar, GUIContent.none);

            // One line above the buttons: who is acting and what they have left.
            int moveLeft = actor.Def.Move - actor.MoveUsedThisTurn;
            if (moveLeft < 0) moveLeft = 0;

            string header = "<b>" + actor.Def.DisplayName + "</b>"
                          + "   生命 " + actor.Hp + "/" + actor.Def.MaxHp
                          + "   行動力 " + actor.Ap + "/" + actor.Def.MaxAp
                          + "   移動 " + moveLeft
                          + (actor.IsGuarding ? "   [格擋中]" : "")
                          + (actor.HasEndedTurn ? "   [已行動]" : "");

            GUIStyle centred = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                fontSize = 13
            };
            GUI.Label(new Rect(bar.x, bar.y - 46f, bar.width, 20f), header, centred);

            // Second line: what the board is waiting for, or why the last thing
            // was refused. A rejection that only reaches the console is invisible
            // to the person holding the mouse.
            string status = _actionMessage;
            if (_armedAction >= 0)
            {
                UnitAction armed = actions[_armedAction];
                status = armed.Target == ActionTarget.Enemy
                    ? "<b>選擇目標</b>　—　點敵人施放，再按一次按鈕或按 Esc 取消"
                    : "<b>確認施放</b>　—　點亮起區域確認，再按一次按鈕或按 Esc 取消";
            }

            if (!string.IsNullOrEmpty(status))
            {
                Color previous = GUI.color;
                GUI.color = _armedAction >= 0 ? ArmedTint : new Color(1f, 0.62f, 0.55f);
                GUI.Label(new Rect(bar.x - 100f, bar.y - 26f, bar.width + 200f, 20f), status, centred);
                GUI.color = previous;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                UnitAction action = actions[i];

                Rect slot = new Rect(bar.x + BarPadding + i * (BarButtonWidth + BarPadding),
                                     bar.y + BarPadding, BarButtonWidth, BarButtonHeight);

                bool affordable = actor.Ap >= action.ApCost;
                bool enabled = actorCanAct && affordable;

                string label = action.Label + "\n<size=10>"
                             + (action.ApCost > 0 ? action.ApCost + " AP" : "免費")
                             + (action.Target == ActionTarget.Enemy ? "  射程 " + action.Range : "")
                             + "  [" + action.ShortcutLabel + "]</size>";

                Color previousBackground = GUI.backgroundColor;
                if (i == _armedAction) GUI.backgroundColor = ArmedTint;
                else if (!affordable) GUI.backgroundColor = UnaffordableTint;

                using (new EditorLikeDisabled(!enabled))
                {
                    if (GUI.Button(slot, new GUIContent(label, action.Hint))) ChooseAction(i);
                }

                GUI.backgroundColor = previousBackground;
            }

            // Tooltip under the bar: the hint the button carries, so the meaning
            // of a skill does not have to be memorised or looked up elsewhere.
            if (!string.IsNullOrEmpty(GUI.tooltip))
                GUI.Label(new Rect(bar.x - 150f, bar.yMax + 2f, bar.width + 300f, 20f), GUI.tooltip, centred);

            Rect endTurn = new Rect(bar.xMax + BarPadding, bar.y + BarPadding, 96f, BarButtonHeight);
            using (new EditorLikeDisabled(_state.CurrentFaction != Faction.Player || _enemyPhaseRunning))
            {
                if (GUI.Button(endTurn, "結束回合\n<size=10>[Space]</size>")) EndPlayerTurn();
            }
        }

        /// <summary>
        /// GUI.enabled as a scope. UnityEditor's DisabledScope is editor-only and
        /// this assembly ships in the player, so the two lines are written out.
        /// </summary>
        private struct EditorLikeDisabled : IDisposable
        {
            private readonly bool _previous;

            public EditorLikeDisabled(bool disabled)
            {
                _previous = GUI.enabled;
                GUI.enabled = _previous && !disabled;
            }

            public void Dispose() => GUI.enabled = _previous;
        }

        // ------------------------------------------------------------------ HUD

        private void OnGUI()
        {
            if (_state == null) return;

            GUI.skin.label.fontSize = 13;
            GUI.skin.label.richText = true;

            // Help covers the board on purpose — you are reading, not playing.
            if (_showHelp) { DrawHelpPanel(); return; }

            DrawNameplates();
            DrawActionBar();
            DrawUnitPanel();

            if (!_showHud) return;

            GUI.skin.label.fontSize = 13;
            GUI.skin.label.richText = true;   // the HUD uses <b> tags
            GUI.Box(new Rect(8, 8, 320, 232), GUIContent.none);
            GUILayout.BeginArea(new Rect(16, 14, 304, 220));

            GUILayout.Label("<b>" + _setup.Encounter.DisplayName + "</b>  turn " + _state.TurnIndex
                            + "  —  " + _state.CurrentFaction + (_enemyPhaseRunning ? " (acting)" : ""));
            GUILayout.Label("objective: " + _state.Objective.Describe()
                            + "   |   enemies left: " + _state.CountLiving(Faction.Enemy));

            if (_state.Outcome != BattleOutcome.InProgress)
                GUILayout.Label("<b>" + (_state.Outcome == BattleOutcome.Victory ? "VICTORY" : "DEFEAT")
                                + "</b>  —  press R to restart");

            GUILayout.Space(4);

            for (int i = 0; i < _state.Units.Count; i++)
            {
                UnitState u = _state.Units[i];
                if (!u.IsAlive) { GUILayout.Label("  u" + u.Id + " " + u.Def.DisplayName + "  DEAD"); continue; }

                string marker = u.Id == _selectedUnitId ? ">" : " ";

                // A Kill objective names a unit, so the roster is the only place a
                // human can find out which one. Without this the objective is
                // unplayable by a person even though the rules resolve it fine.
                string flags = (u.IsObjectiveTarget ? " <b>«TARGET»</b>" : "")
                             + (u.IsGuarding ? " GUARD" : "")
                             + (u.HasEndedTurn ? " done" : "");

                // MOVE is a per-turn budget now, so the remaining cells matter as
                // much as remaining AP. Without this the range just silently
                // stops growing and there is no way to tell why.
                int moveLeft = u.Def.Move - u.MoveUsedThisTurn;
                if (moveLeft < 0) moveLeft = 0;

                GUILayout.Label(marker + " u" + u.Id + " " + u.Def.DisplayName
                                + "  HP " + u.Hp + "/" + u.Def.MaxHp
                                + "  AP " + u.Ap + "/" + u.Def.MaxAp
                                + "  MOVE " + moveLeft + "/" + u.Def.Move
                                + "  " + u.Position + flags);
            }

            GUILayout.EndArea();

            DrawCellInspector();
            DrawControls();
            DrawLog();
        }

        private void DrawCellInspector()
        {
            if (!_inspectedCell.HasValue) return;

            Coord c = _inspectedCell.Value;
            TerrainDef terrain = _state.Map.TerrainAt(c);

            GUI.Box(new Rect(8, 248, 320, 96), GUIContent.none);
            GUILayout.BeginArea(new Rect(16, 254, 304, 86));
            GUILayout.Label("<b>cell " + c + "</b>  " + terrain.Name
                            + (terrain.BlocksMovement ? "  (blocking)" : "  cost " + terrain.MovementCost));
            GUILayout.Label("static exposure : " + _inspectedStaticExposure);
            GUILayout.Label("threatened by   : " + _inspectedThreatCount + " enemy(s)");
            if (_reach != null && _reach.CanReach(c))
                GUILayout.Label("move cost       : " + _reach.CostTo(c) + " AP");
            GUILayout.EndArea();
        }

        private void DrawControls()
        {
            GUI.Box(new Rect(8, Screen.height - 129, 820, 121), GUIContent.none);
            GUILayout.BeginArea(new Rect(16, Screen.height - 123, 804, 109));
            GUILayout.Label("click cell = move (costs AP <b>and</b> MOVE)  |  click enemy = attack (4 AP)");
            GUILayout.Label("actions are on the bar at the bottom — aimed skills: click the button, then the enemy");
            GUILayout.Label("space = end turn  |  TAB = their ranges (" + _enemyOverlay
                            + ")  |  Z = yours (" + (_showOwnRanges ? "ON" : "off") + ")  |  R = restart");
            // The map-switch row is inert during an editor playtest — the map
            // under test only exists in the editor, so there is no key back to it.
            if (_isEditorPlaytest)
            {
                GUILayout.Label("<b>editor playtest</b> — map switching is off; press Stop in the editor to go back");
            }
            else
            {
                GUILayout.Label(MapKeyHints(0, 5));
                GUILayout.Label(MapKeyHints(5, 10));
                GUILayout.Label(MapKeyHints(10, MapSelectLabels.Length));
            }
            GUILayout.EndArea();
        }

        /// <summary>Keys that select a map, in slot order. Index i selects entry i.</summary>
        private static readonly Key[] MapSelectKeys = BattleKeys.MapSelect;

        /// <summary>What to print for each of those keys.</summary>
        private static readonly string[] MapSelectLabels =
        { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "[", "]" };

        /// <summary>
        /// The map-select row, built from the list itself.
        ///
        /// It used to be a hardcoded string and had drifted four entries out of
        /// date, telling players that 2 was big-north when it had been gym-lanes
        /// for two rounds. Generating it means it cannot go stale again.
        /// </summary>
        private static string MapKeyHints(int from, int to)
        {
            string[] all = PrototypeBootstrap.SelectableEncounters;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int i = from; i < to && i < all.Length && i < MapSelectLabels.Length; i++)
            {
                string name = all[i];
                if (name.EndsWith(".encounter")) name = name.Substring(0, name.Length - ".encounter".Length);
                if (name.StartsWith("gym-")) name = name.Substring("gym-".Length);

                if (sb.Length > 0) sb.Append("  ");
                sb.Append("<b>").Append(MapSelectLabels[i]).Append("</b> ").Append(name);
            }

            return sb.ToString();
        }

        private void DrawLog()
        {
            const int lines = 14;
            float w = 340f;
            GUI.Box(new Rect(Screen.width - w - 8, 8, w, lines * 17 + 14), GUIContent.none);
            GUILayout.BeginArea(new Rect(Screen.width - w, 14, w - 14, lines * 17));

            int start = Mathf.Max(0, _log.Count - lines);
            for (int i = start; i < _log.Count; i++) GUILayout.Label(_log[i]);

            GUILayout.EndArea();
        }
    }
}
