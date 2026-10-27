using System;
using System.Collections.Generic;
using Project.Match3;

namespace Project.Tutorial
{
    /// <summary>Only disposable scripted demo state. No account, economy, clock, transport or persistence dependencies.</summary>
    public sealed class TutorialSession
    {
        public const int StepCount = 12;
        public readonly Match3BoardLogic Board = new Match3BoardLogic();
        public int Step { get; private set; }
        public int Hp { get; private set; }
        public int Mana { get; private set; }
        public int EnemyHp { get; private set; }
        public int ShieldStacks { get; private set; }
        public bool Passed { get; private set; }
        public bool FuryActive { get; private set; }
        public bool ExtraTurn { get; private set; }
        public bool PetardPending => _pendingPetardDamage > 0;
        public bool Targeting => _targeting.HasValue;
        public string Feedback { get; private set; }
        public List<(int x, int y)> LastArea { get; private set; } = new List<(int, int)>();
        private AbilityType? _targeting;
        private int _pendingPetardDamage;
        public TutorialSession() { Restart(); }
        public void Restart() => Enter(0);
        public bool Next() { if (!Passed || Step == StepCount - 1) return false; Enter(Step + 1); return true; }
        public bool Back() { if (Step == 0) return false; Enter(Step - 1); return true; }
        private void Enter(int step)
        {
            Step = step; Hp = step == 4 ? 60 : step == 8 ? 50 : 90; Mana = step == 1 ? 0 : step == 9 ? 60 : step == 8 ? 50 : 20;
            EnemyHp = TutorialRules.MaxHp; ShieldStacks = 0; FuryActive = ExtraTurn = false; _targeting = null;
            Passed = step == 0 || step == StepCount - 1; Feedback = ""; LastArea.Clear(); _pendingPetardDamage = 0;
            var type = step == 3 ? PieceType.Skull : step == 4 ? PieceType.Ankh : step == 5 ? PieceType.GemYellow : PieceType.GemRed;
            Board.Init(771 + step); Board.FromArray(DemoBoard(type, step == 5 ? 5 : 3));
        }
        public bool Swap(int x0, int y0, int x1, int y1, out List<MatchResult> matches)
        {
            matches = null;
            if (Passed || Step != 1 && Step != 3 && Step != 4 && Step != 5) return Reject("Следуйте текущей подсказке.");
            var expected = x0 == 2 && x1 == 2 && (y0 == 2 && y1 == 3 || y0 == 3 && y1 == 2);
            if (!expected || !Board.TrySwap(x0, y0, x1, y1, out matches)) return Reject("Поменяйте местами два подсвеченных предмета.");
            var cells = new HashSet<int>();
            foreach (var match in matches)
            {
                if (match.count >= 5) ExtraTurn = true;
                foreach (var cell in match.cells) cells.Add(cell.Item2 * 6 + cell.Item1);
            }
            var mana = 0; var heal = 0; var damage = 0;
            foreach (var index in cells)
            {
                var type = Board[index % 6, index / 6]; mana += TutorialRules.Mana(type);
                if (type == PieceType.Ankh) heal += TutorialRules.CapsuleHeal;
                if (type == PieceType.Skull) damage += TutorialRules.BombDamage;
            }
            Mana = Math.Min(TutorialRules.MaxMana, Mana + mana); Hp = Math.Min(TutorialRules.MaxHp, Hp + heal); EnemyHp = Math.Max(0, EnemyHp - damage);
            Feedback = Step == 1 ? "+15 маны за три канистры." : Step == 3 ? "В примере без брони: 3 бомбы = 15 урона." :
                Step == 4 ? "+3 здоровья за три капсулы." : "+15 маны. Линия из 5 даёт дополнительный ход!";
            Passed = true; return true;
        }
        public bool ChooseResource(PieceType type)
        {
            if (Step != 2 || Passed || type != PieceType.GemYellow) return Reject("Найдите компас — он даёт 3 маны за предмет.");
            Feedback = "Верно! Канистра: 5 маны, компас: 3, рюкзак: 1."; Passed = true; return true;
        }
        public bool Ability(AbilityType ability)
        {
            if (Passed || PetardPending) return Reject("Действие уже выполнено. Дождитесь результата.");
            var allowed = Step == 6 && ability == AbilityType.Cross || Step == 7 && ability == AbilityType.Square || Step == 8 && ability == AbilityType.Shield ||
                Step == 9 && (!FuryActive && ability == AbilityType.Fury || FuryActive && ability == AbilityType.Petard);
            if (!allowed || Mana < TutorialRules.Cost(ability)) return Reject("Сейчас нужна подсвеченная способность.");
            if (ability == AbilityType.Cross || ability == AbilityType.Square)
            { _targeting = ability; Feedback = "Теперь нажмите центр подсвеченной области."; return true; }
            Mana -= TutorialRules.Cost(ability);
            if (ability == AbilityType.Shield)
            {
                ShieldStacks = 1; Hp -= TutorialRules.PetardDamage - TutorialRules.ShieldArmor;
                Feedback = "Щит: удар 15 − 4 брони = 11 урона. Ход остаётся вашим."; Passed = true;
            }
            else if (ability == AbilityType.Fury)
            { FuryActive = true; Feedback = "Ярость готова. Теперь примените петарду — ход всё ещё ваш."; }
            else
            {
                _pendingPetardDamage = TutorialRules.PetardDamage + CountBombs();
                Feedback = "Петарда летит к манекену. Урон появится при попадании.";
            }
            return true;
        }
        /// <summary>The offline presentation calls this once at actual projectile contact.</summary>
        public bool ResolvePetardImpact()
        {
            if (Step != 9 || !PetardPending || Passed) return false;
            var damage = _pendingPetardDamage; _pendingPetardDamage = 0;
            EnemyHp = Math.Max(0, EnemyHp - damage); Passed = true;
            Feedback = "Петарда: " + damage + " урона в этом примере без крита. Поле не изменилось.";
            return true;
        }
        public bool Target(int x, int y)
        {
            if (!_targeting.HasValue || x != 2 || y != 2 || Passed) return Reject("Нажмите подсвеченный центр области.");
            var ability = _targeting.Value; var before = Board.ToArray();
            Mana -= TutorialRules.Cost(ability); LastArea = Board.ApplyAbility(ability, x, y);
            var damage = TutorialRules.AbilityDamage;
            foreach (var cell in LastArea)
            {
                var type = (PieceType)before[cell.y * 6 + cell.x]; Mana = Math.Min(TutorialRules.MaxMana, Mana + TutorialRules.Mana(type));
                if (type == PieceType.Ankh) Hp = Math.Min(TutorialRules.MaxHp, Hp + TutorialRules.CapsuleHeal);
                if (type == PieceType.Skull) damage += TutorialRules.BombDamage;
            }
            EnemyHp = Math.Max(0, EnemyHp - damage); _targeting = null; Passed = true;
            Feedback = ability == AbilityType.Cross ? "Крест собрал 9 клеток: по 2 от центра в каждую сторону." : "Квадрат собрал 9 клеток: область 3 × 3.";
            return true;
        }
        public bool ChooseMode(bool pro)
        {
            if (Step != 10 || Passed || !pro) return Reject("Снаряжение учитывается в «Дуэли Pro» и шахте.");
            Passed = true; Feedback = "Верно. В обычной дуэли базовые условия равны для всех."; return true;
        }
        public int CountBombs() { var count = 0; foreach (var value in Board.ToArray()) if (value == (int)PieceType.Skull) count++; return count; }
        private bool Reject(string text) { Feedback = text; return false; }
        public static int[] DemoBoard(PieceType type, int length)
        {
            var start = length == 5 ? 0 : 1;
            for (var seed = 1; seed < 10000; seed++)
            {
                var board = new Match3BoardLogic(); board.Init(seed + (int)type * 91 + length * 71); var data = board.ToArray();
                for (var x = start; x < start + length; x++) data[2 * 6 + x] = (int)type;
                var other = (int)type % 5 + 1; data[2 * 6 + 2] = other; data[3 * 6 + 2] = (int)type;
                if (start > 0) data[2 * 6 + start - 1] = other;
                if (start + length < 6) data[2 * 6 + start + length] = other;
                board.FromArray(data); if (board.FindMatches().Count != 0) continue;
                if (board.TrySwap(2, 2, 2, 3, out var matches) && matches.Count == 1 && matches[0].type == type && matches[0].count == length) return data;
            }
            throw new InvalidOperationException("Could not prepare a clean tutorial demonstration.");
        }
    }
}
