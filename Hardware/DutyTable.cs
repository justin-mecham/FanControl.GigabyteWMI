using System;

namespace FanControl.GigabyteWMI.Hardware
{
    /// <summary>
    /// Official Gigabyte 16-step hardware duty register lookup table extracted from ComData.dll.
    /// Maps linear user percentage (0%-100%) to EC register speeds (57 to 229).
    /// </summary>
    public static class DutyTable
    {
        public static readonly byte[] Table = new byte[] {
            57, 68, 80, 91, 103, 114, 125, 137, 148, 160, 171, 183, 194, 206, 217, 229
        };

        public static byte ToRegisterSpeed(float percentage)
        {
            int index = (int)Math.Round((percentage / 100.0) * (Table.Length - 1));
            index = Math.Max(0, Math.Min(Table.Length - 1, index));
            return Table[index];
        }

        public static int ToStepIndex(float percentage)
        {
            int index = (int)Math.Round((percentage / 100.0) * (Table.Length - 1));
            return Math.Max(0, Math.Min(Table.Length - 1, index));
        }
    }
}
