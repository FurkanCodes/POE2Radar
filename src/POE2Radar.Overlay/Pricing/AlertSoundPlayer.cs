// Source-port of RitualHelper GPLv3 code; see RitualHelper.GPLv3.LICENSE.txt in this folder.
using System;
using System.Runtime.InteropServices;

namespace POE2Radar.Overlay.Pricing
{
    internal static class AlertSoundPlayer
    {
        private const uint MbIconHand = 0x00000010;
        private const uint MbIconQuestion = 0x00000020;
        private const uint MbIconExclamation = 0x00000030;
        private const uint MbIconAsterisk = 0x00000040;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MessageBeep(uint uType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Beep(uint frequency, uint duration);

        // 0 = Asterisk, 1 = Exclamation, 2 = Hand, 3 = Question, 4 = Beep
        public static void Play(int soundType)
        {
            try
            {
                if (soundType == 4)
                {
                    if (!Beep(880, 180))
                        Console.Beep(880, 180);
                    return;
                }

                var beepType = soundType switch
                {
                    1 => MbIconExclamation,
                    2 => MbIconHand,
                    3 => MbIconQuestion,
                    _ => MbIconAsterisk,
                };

                if (!MessageBeep(beepType))
                    PlaySystemSoundFallback(soundType);
            }
            catch
            {
                try { PlaySystemSoundFallback(soundType); } catch { }
            }
        }

        private static void PlaySystemSoundFallback(int soundType)
        {
            var frequency = soundType switch
            {
                1 => 740,
                2 => 520,
                3 => 660,
                4 => 880,
                _ => 880,
            };
            Console.Beep(frequency, 180);
        }
    }
}

