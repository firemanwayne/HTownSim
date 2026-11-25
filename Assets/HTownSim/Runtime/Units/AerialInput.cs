#nullable enable

namespace HTownSim.Runtime
{
    /// <summary>One frame of aerial operator input, each -1..1 (positive = out, down, raise, extend, rotate right).</summary>
    internal readonly struct AerialInput
    {
        internal AerialInput(float outriggerOut, float outriggerDown, float raise, float extend, float slew)
        {
            OutriggerOut = outriggerOut;

            OutriggerDown = outriggerDown;

            Raise = raise;

            Extend = extend;

            Slew = slew;
        }

        /// <summary>Outrigger beams: +1 extend, -1 retract.</summary>
        internal float OutriggerOut { get; }

        /// <summary>Outrigger jacks: +1 lower to the ground, -1 raise.</summary>
        internal float OutriggerDown { get; }

        /// <summary>Ladder: +1 raise, -1 lower.</summary>
        internal float Raise { get; }

        /// <summary>Fly sections: +1 extend, -1 retract.</summary>
        internal float Extend { get; }

        /// <summary>Turntable: +1 right, -1 left.</summary>
        internal float Slew { get; }
    }
}
