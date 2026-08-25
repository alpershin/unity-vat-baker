using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// Reads a single-layer AnimatorController into VAT source clips. What crosses over is data —
    /// which clips, the names their states answer to, their speed and their fade-in times. The state
    /// machine itself does not: parameters, conditions and exit times stay behind as C# you write.
    /// </summary>
    internal static class AnimatorControllerConverter
    {
        public static List<VatSourceClip> Convert(AnimatorController controller, int layerIndex, List<string> skipped)
        {
            var clips = new List<VatSourceClip>();
            if (controller == null || controller.layers.Length == 0)
            {
                return clips;
            }

            var index = Mathf.Clamp(layerIndex, 0, controller.layers.Length - 1);
            if (controller.layers.Length > 1)
            {
                skipped.Add($"{controller.layers.Length - 1} extra layer(s): VAT has no layering, only layer {index} was read.");
            }

            var machine = controller.layers[index].stateMachine;
            var states = new List<AnimatorState>();
            var machines = new List<AnimatorStateMachine>();
            Collect(machine, states, machines);

            var fades = CollectFadeDurations(states, machines);
            var defaultState = machine.defaultState;

            for (var i = 0; i < states.Count; i++)
            {
                var state = states[i];
                if (!(state.motion is AnimationClip clip))
                {
                    skipped.Add($"state '{state.name}': {DescribeMotion(state.motion)} cannot be baked.");
                    continue;
                }

                var fade = fades.TryGetValue(state, out var duration) ? duration : 0f;
                var record = new VatSourceClip(clip, state.name, Mathf.Max(state.speed, 0.01f), fade);

                // The default state goes first so index 0 is what a fresh animator starts on.
                if (state == defaultState)
                {
                    clips.Insert(0, record);
                    continue;
                }

                clips.Add(record);
            }

            return clips;
        }

        // Sub-state machines are flattened: VAT has no nesting, and a state's name stays unique
        // enough to call by.
        private static void Collect(AnimatorStateMachine machine, List<AnimatorState> states, List<AnimatorStateMachine> machines)
        {
            machines.Add(machine);

            for (var i = 0; i < machine.states.Length; i++)
            {
                states.Add(machine.states[i].state);
            }

            for (var i = 0; i < machine.stateMachines.Length; i++)
            {
                Collect(machine.stateMachines[i].stateMachine, states, machines);
            }
        }

        /// <summary>
        /// The fade-in a state is usually entered with. Several transitions can lead to one state
        /// with different times, so the longest is taken: it is the one that would look wrong if a
        /// shorter value were used.
        /// </summary>
        private static Dictionary<AnimatorState, float> CollectFadeDurations(List<AnimatorState> states, List<AnimatorStateMachine> machines)
        {
            var fades = new Dictionary<AnimatorState, float>();

            for (var i = 0; i < states.Count; i++)
            {
                var source = states[i];
                var sourceLength = source.motion != null ? source.motion.averageDuration : 1f;
                Accumulate(fades, source.transitions, sourceLength);
            }

            // AnyState transitions are how most simple controllers reach hits and deaths, so their
            // fade times matter as much as the ones drawn between states.
            for (var i = 0; i < machines.Count; i++)
            {
                Accumulate(fades, machines[i].anyStateTransitions, 1f);
            }

            return fades;
        }

        private static void Accumulate(Dictionary<AnimatorState, float> fades, AnimatorStateTransition[] transitions, float sourceLength)
        {
            for (var i = 0; i < transitions.Length; i++)
            {
                var transition = transitions[i];
                if (transition.destinationState == null)
                {
                    continue;
                }

                // A non-fixed duration is a fraction of the state being left, not seconds.
                var duration = transition.hasFixedDuration
                    ? transition.duration
                    : transition.duration * Mathf.Max(sourceLength, 0.01f);

                if (!fades.TryGetValue(transition.destinationState, out var known) || duration > known)
                {
                    fades[transition.destinationState] = duration;
                }
            }
        }

        private static string DescribeMotion(Motion motion)
        {
            if (motion == null)
            {
                return "an empty state";
            }

            return motion is BlendTree ? "a blend tree" : motion.GetType().Name;
        }
    }
}
