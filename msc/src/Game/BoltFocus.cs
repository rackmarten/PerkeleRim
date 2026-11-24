using UnityEngine;

namespace PerkeleRim.Game
{
    // The bolt MSC's tools are on (docs/FSM_MAP.md, "Building the Satsuma"): with the tools out, MSC's
    // 2Spanner/Raycast :: Check mouse-picks a bolt (BoltPM, 1 m, layer 12) and gives its mesh the "activebolt"
    // material until the crosshair leaves it, whatever the wrench's size (only the right size turns it). Skyrim draws
    // the parts' bolts as they were read, so ItemLink sends this one as an item of its own, with MSC's highlight and
    // live pose: Skyrim shows which bolt the wrench is on and how it turns in or out.
    public class BoltFocus
    {
        const string CheckPath = "Pivot/AnimPivot/Camera/FPSCamera/2Spanner/Raycast";  // below PLAYER

        PlayMakerFSM check;
        float nextFind;

        // The highlighted bolt's mesh, or null.
        public Transform Mesh()
        {
            if (check == null)
            {
                if (Time.realtimeSinceStartup < nextFind) return null;
                nextFind = Time.realtimeSinceStartup + 5f;
                var player = GameObject.Find("PLAYER");
                var raycast = player != null ? player.transform.Find(CheckPath) : null;  // found while the tools are away too
                if (raycast == null) return null;
                foreach (var f in raycast.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Check") check = f;
                if (check == null) return null;
            }
            if (!check.gameObject.activeInHierarchy) return null;
            var mesh = check.FsmVariables.FindFsmGameObject("Mesh");
            var go = mesh != null ? mesh.Value : null;
            if (go == null || !go.activeInHierarchy) return null;
            var r = go.GetComponent<Renderer>();
            if (r == null || !r.enabled || r.sharedMaterial == null) return null;
            return r.sharedMaterial.name.StartsWith("activebolt") ? go.transform : null;
        }
    }
}
