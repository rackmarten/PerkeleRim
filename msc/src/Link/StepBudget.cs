using System;
using System.Collections;

namespace PerkeleRim
{
    // Runs work split into small steps (an iterator, one MoveNext a step) a frame's worth at a time:
    // steps until BudgetMs of this frame went by, the rest next frame. Several pieces of work can
    // share a frame (many small models): each Run goes on from where the frame's budget stands. A
    // frame then costs the budget plus at most one step, so the steps must stay small (LongestStepMs
    // says how small they were). Unity-free: the caller gives the clock, in milliseconds.
    public sealed class StepBudget
    {
        readonly Func<double> clock;
        double frameStart;

        public double BudgetMs;
        public double LongestStepMs { get; private set; }
        public string LongestStep { get; private set; } = "";  // what the work said it was doing then (Current)

        public StepBudget(double budgetMs, Func<double> clock)
        {
            BudgetMs = budgetMs;
            this.clock = clock;
            frameStart = clock();
        }

        // A new frame: the budget is whole again.
        public void BeginFrame() => frameStart = clock();

        public bool Spent => clock() - frameStart >= BudgetMs;

        public void ResetLongest()
        {
            LongestStepMs = 0;
            LongestStep = "";
        }

        // Steps of work until it ends (true) or this frame's budget is spent (false). Right after
        // BeginFrame at least one step runs, so work always gets on.
        public bool Run(IEnumerator work)
        {
            do
            {
                double before = clock();
                bool more = work.MoveNext();
                double ms = clock() - before;
                if (ms > LongestStepMs)
                {
                    LongestStepMs = ms;
                    LongestStep = work.Current as string ?? "";
                }
                if (!more) return true;
            } while (!Spent);
            return false;
        }
    }
}
