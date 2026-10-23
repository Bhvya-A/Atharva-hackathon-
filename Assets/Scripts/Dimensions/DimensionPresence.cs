using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Enables or disables this object (and optional extra targets) based on the active dimension.
    /// The manager locates inactive instances, so this component may disable its own GameObject.
    /// </summary>
    public class DimensionPresence : MonoBehaviour
    {
        [SerializeField] DimensionMembership membership = DimensionMembership.Both;
        [SerializeField] GameObject[] additionalTargets;

        public DimensionMembership Membership => membership;

        public void Configure(DimensionMembership assigned, params GameObject[] targets)
        {
            membership = assigned;
            additionalTargets = targets;
        }

        public bool BelongsTo(Dimension dimension)
        {
            switch (membership)
            {
                case DimensionMembership.Both:
                    return true;
                case DimensionMembership.Reality:
                    return dimension == Dimension.Reality;
                case DimensionMembership.Void:
                    return dimension == Dimension.Void;
                default:
                    return false;
            }
        }

        public void Apply(Dimension dimension)
        {
            bool visible = BelongsTo(dimension);

            if (additionalTargets != null)
            {
                for (int i = 0; i < additionalTargets.Length; i++)
                {
                    GameObject target = additionalTargets[i];
                    if (target != null && target != gameObject && target.activeSelf != visible)
                        target.SetActive(visible);
                }
            }

            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}
