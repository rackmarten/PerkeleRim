using UnityEngine;

namespace PerkeleRim.Game
{
    // While a throw is charged (ThrowCharge) the item MSC's hand holds is pulled back toward the
    // player along the view, up to ThrowCharge.WindBackMax at a full charge, and put back where it
    // was the moment the button is let go (so it leaves from there, MSC's throw a frame or two
    // later). MSC places the hand (1Hand_Assemble/Hand, which holds the item with a joint) and
    // ItemPivot (which the item is parented to) once when the item is picked up and leaves them
    // be, so both move together by the same offset and the item follows without fighting the joint.
    // The pulled-back item is what ItemLink sends to Skyrim, so the hand layer there shows it.
    public class ThrowWindUp
    {
        Transform hand, pivot;
        Vector3 handBase, pivotBase;  // their local positions before the pull
        Vector3 handSet, pivotSet;    // and as the pull left them
        bool pulled;

        // Call every LateUpdate before the items are sent. now: realtime seconds, as ThrowCharge.Apply got.
        public void Update(ThrowCharge charge, float now)
        {
            if (!charge.Charging)
            {
                Restore();
                return;
            }
            var fsm = PocketLink.PickUp();
            var cam = Camera.main;
            if (fsm == null || cam == null || fsm.transform.parent == null) return;
            var h = fsm.transform;
            if (!pulled)
            {
                var p = h.parent.Find("ItemPivot");
                if (p == null) return;
                hand = h;
                pivot = p;
                handBase = h.localPosition;
                pivotBase = p.localPosition;
                pulled = true;
            }
            var forward = cam.transform.forward;
            float distance = Vector3.Dot(hand.parent.TransformPoint(handBase) - cam.transform.position, forward);
            float back = ThrowCharge.WindBack(ThrowCharge.Fraction(charge.Held(now)), distance);
            var offset = hand.parent.InverseTransformVector(-forward * back);
            hand.localPosition = handSet = handBase + offset;
            pivot.localPosition = pivotSet = pivotBase + offset;
        }

        void Restore()
        {
            if (!pulled) return;
            pulled = false;
            // Only what is still where the pull left it: a dropped item sends the hand back to its
            // own place (MSC's Wait state), which stays.
            if (hand != null && hand.localPosition == handSet) hand.localPosition = handBase;
            if (pivot != null && pivot.localPosition == pivotSet) pivot.localPosition = pivotBase;
        }
    }
}
