using System;

namespace PerkeleRim
{
    // What waiting or sleeping in Skyrim (TimePassed) does to MSC's player: the needs and the clock.
    // The rates are MSC's own, from its bed (docs/FSM_MAP.md, "Sleep"): a bed sleeps 2, 4, 6, 8 or 10
    // hours by how tired the player is, takes the fatigue to 0 and adds Rate = fatigue / 5 to hunger,
    // thirst and urine over those hours. A Skyrim bed uses the same rate per hour for any length of
    // sleep; waiting without a bed rests half as fast and hungers the same. Unity-free; hours are
    // game hours, clock times minutes since midnight.
    public static class Rest
    {
        // Fatigue a wait takes off compared with a bed, per hour.
        public const double WaitShare = 0.5;

        // MSC's sleep length by fatigue.
        public static int BedHours(double fatigue)
        {
            if (fatigue < 20) return 2;
            if (fatigue < 40) return 4;
            if (fatigue < 60) return 6;
            if (fatigue < 80) return 8;
            return 10;
        }

        public struct Needs
        {
            public double Fatigue;  // added (<= 0)
            public double Hunger;   // added (>= 0)
            public double Thirst;
            public double Urine;
        }

        // The change of the needs after `hours` of rest, from the fatigue at the start. Nothing for
        // a player who isn't tired: MSC wouldn't sleep either.
        public static Needs Hours(double fatigue, double hours, bool bed)
        {
            var n = new Needs();
            if (fatigue <= 0 || hours <= 0) return n;
            int bedHours = BedHours(fatigue);
            double drop = fatigue / bedHours * hours * (bed ? 1.0 : WaitShare);
            n.Fatigue = -Math.Min(drop, fatigue);
            double rise = fatigue / 5.0 / bedHours * hours;
            n.Hunger = n.Thirst = n.Urine = rise;
            return n;
        }

        // ---- The clock ----

        public const double Day = 1440.0;

        // MSC's sun is a state per 2-hour block, "00-02" to "22-00" (MAP/SUN/Pivot/SUN :: Color), that
        // sets the sun's angle, `Time` (the block's start hour, 24 for "00-02") and GlobalHour. The
        // clock (:: Clock) has a state per hour, "1" to "24" (24 is the hour after midnight).
        public struct Time
        {
            public double Sun;    // minutes since midnight, 0..1440
            public double Clock;  // the same on the display clock
            public int Day;       // 1..7
        }

        // Time moved forward by `hours`: the day counts the midnights the sun crosses.
        public static Time Advance(Time t, double hours)
        {
            double minutes = hours * 60.0;
            double sun = t.Sun + minutes;
            int midnights = (int)Math.Floor(sun / Day);
            return new Time
            {
                Sun = Wrap(sun),
                Clock = Wrap(t.Clock + minutes),
                Day = ((t.Day - 1 + midnights) % 7 + 7) % 7 + 1,
            };
        }

        static double Wrap(double minutes)
        {
            double m = minutes % Day;
            return m < 0 ? m + Day : m;
        }

        // Color state of a sun time, "18-20".
        public static string ColorState(double sun)
        {
            int start = Block(sun);
            return $"{start:00}-{(start + 2) % 24:00}";
        }

        // The Color FSM's `Time` for that block: its start hour, 24 for "00-02".
        public static int ColorTime(double sun)
        {
            int start = Block(sun);
            return start == 0 ? 24 : start;
        }

        // Minutes into the sun's block.
        public static double BlockMinutes(double sun) => sun - Block(sun) * 60.0;

        // `Rotation` of MAP/SUN/Pivot at the start of the block (Color's state sets it), and how far
        // MSC's Rotation FSM turns it per block minute (0.5 degrees every 5 seconds the minutes grow
        // by 1).
        public static double RotationStart(double sun) => (Block(sun) * 15.0 + 150.0) % 360.0;
        public const double RotationPerMinute = 0.5;

        public static double Rotation(double sun) => RotationStart(sun) + RotationPerMinute * BlockMinutes(sun);

        // Clock state name of a display clock time, "1" to "24".
        public static string ClockState(double clock)
        {
            int hour = (int)Math.Floor(clock / 60.0) % 24;
            return (hour == 0 ? 24 : hour).ToString();
        }

        public static double ClockHours(double clock) => Math.Floor(clock / 60.0) % 24 == 0 ? 24 : Math.Floor(clock / 60.0) % 24;

        public static double ClockMinutes(double clock) => clock - Math.Floor(clock / 60.0) * 60.0;

        // Display clock minutes from the Clock FSM's Hours (1..24) and Minutes.
        public static double ClockOf(double hours, double minutes) => Wrap((hours % 24) * 60.0 + minutes);

        // Sun minutes from the Color FSM's Time (start hour, 24 for "00-02") and Minutes.
        public static double SunOf(double time, double minutes) => Wrap((time % 24) * 60.0 + minutes);

        static int Block(double sun) => (int)(Math.Floor(Wrap(sun) / 120.0) * 2);
    }
}
