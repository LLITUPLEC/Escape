namespace Project.Duel
{
    public static class BullsCowsScoring
    {
        public static (int bulls, int cows) Score(string pin, string guess)
        {
            var n = pin.Length;
            if (string.IsNullOrEmpty(guess) || guess.Length != n)
                return (0, 0);

            var bulls = 0;
            var counts = new int[10];
            for (var i = 0; i < n; i++)
            {
                if (pin[i] == guess[i])
                    bulls++;
                else
                    counts[pin[i] - '0']++;
            }

            var cows = 0;
            for (var i = 0; i < n; i++)
            {
                if (pin[i] == guess[i]) continue;
                var d = guess[i] - '0';
                if (d < 0 || d > 9 || counts[d] <= 0) continue;
                counts[d]--;
                cows++;
            }

            return (bulls, cows);
        }

        public static string FormatAttemptLine(int attemptNumber, string guess, int bulls, int cows)
        {
            string bullsColor;
            string cowsColor;
            if (bulls >= 2)
            {
                bullsColor = "#4ADE80";
                cowsColor = "#6B7280";
            }
            else if (bulls + cows >= 2)
            {
                bullsColor = "#F59E0B";
                cowsColor = "#F59E0B";
            }
            else
            {
                bullsColor = "#6B7280";
                cowsColor = "#DC2626";
            }

            return $"{attemptNumber})  <color=#6B7280>{guess}</color>  <color=#3D3225>\u2014</color>  " +
                   $"<b><color={bullsColor}>{bulls}</color></b> <color=#3D3225>:</color> " +
                   $"<b><color={cowsColor}>{cows}</color></b>";
        }
    }
}
