using UnityEngine;

namespace Backpacking.Character
{
    /// <summary>
    /// Adjusts the hiker's animated pose with humanoid IK, after the clips have played: the head and shoulders
    /// turn towards where you're looking, feet stand on the slope under them, and when seated the hiker sits on
    /// the ground with legs stretched out in front and hands planted behind. Lives on the model, next to its
    /// Animator, and is driven by <see cref="PlayerAvatar"/>.
    /// </summary>
    [ExecuteAlways] // so editor snapshots (CharacterSnapshots) show the pose too
    public class HikerPose : MonoBehaviour
    {
        /// <summary>0 standing, 1 fully seated on the ground. Blend it in over the sit-down.</summary>
        public float Seated { get; set; }
        /// <summary>A point the hiker looks at, or null to look where the animation looks.</summary>
        public Vector3? LookTarget { get; set; }
        /// <summary>
        /// Plant the feet (and seated hands) on the ground under them. Off for the creator preview and editor
        /// snapshots, which stand on nothing in particular.
        /// </summary>
        public bool GroundFeet { get; set; }
        /// <summary>Where the right hand reaches to hold an item out, or null to leave the arm to the animation.</summary>
        public Vector3? HandTarget { get; set; }
        public float HandWeight { get; set; }
        /// <summary>The held item's frame, for bending the elbow out and down naturally.</summary>
        public Quaternion HandFrame { get; set; } = Quaternion.identity;

        Animator animator;
        Transform ground;
        float legLength;
        float lookWeight;
        float lowerBody;
        readonly RaycastHit[] hits = new RaycastHit[8];
        Quaternion leftToesRest = Quaternion.identity, rightToesRest = Quaternion.identity;

        void Awake()
        {
            animator = GetComponent<Animator>();
            // Added right after the model is made, so the bones are still in their rest pose: toes straight.
            if (animator != null && animator.isHuman)
            {
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftToes), right = animator.GetBoneTransform(HumanBodyBones.RightToes);
                if (left != null)
                    leftToesRest = left.localRotation;
                if (right != null)
                    rightToesRest = right.localRotation;
            }
            // The object the hiker stands on: the avatar root, whose origin is at the soles.
            ground = transform.parent != null ? transform.parent : transform;
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !animator.isHuman)
                return;
            float scale = ground.lossyScale.y;
            MeasureLegs();

            lookWeight = Mathf.MoveTowards(lookWeight, LookTarget.HasValue ? 1f : 0f, Time.deltaTime * 3f);
            if (lookWeight > 0f && LookTarget.HasValue)
            {
                animator.SetLookAtPosition(LookTarget.Value);
                // Mostly the head, a little the chest; seated, the body stays put.
                animator.SetLookAtWeight(lookWeight * 0.9f, Mathf.Lerp(0.2f, 0.05f, Seated), 0.7f, 0f, 0.55f);
            }

            HoldOut();

            float seated = Mathf.SmoothStep(0f, 1f, Seated);
            if (seated > 0f)
                Sit(seated, scale);
            else if (GroundFeet)
                PlantFeet(scale);
            else
                lowerBody = 0f;
        }

        /// <summary>Raises the right hand to hold an item, the elbow out and down behind it.</summary>
        void HoldOut()
        {
            if (!HandTarget.HasValue || HandWeight <= 0f)
            {
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
                return;
            }
            float weight = Mathf.SmoothStep(0f, 1f, HandWeight);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, HandTarget.Value);
            Transform shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (shoulder != null)
            {
                animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight);
                animator.SetIKHintPosition(AvatarIKHint.RightElbow,
                    Vector3.Lerp(shoulder.position, HandTarget.Value, 0.5f) + HandFrame * new Vector3(0.25f, -0.3f, -0.1f));
            }
        }

        /// <summary>Hip joint to ankle, measured while standing so it's right for either body and any scale.</summary>
        void MeasureLegs()
        {
            if (Seated > 0f)
                return;
            Transform hip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (hip != null && knee != null && foot != null)
                legLength = Vector3.Distance(hip.position, knee.position) + Vector3.Distance(knee.position, foot.position);
        }

        /// <summary>
        /// The sitting clip sits on a chair. Drop the body until the seat is on the ground, stretch the legs out
        /// in front with the heels down and toes up, and lean back on both hands.
        /// </summary>
        void Sit(float weight, float scale)
        {
            Vector3 up = ground.up, forward = ground.forward, right = ground.right;
            float floor = Vector3.Dot(ground.position, up);
            float legs = legLength > 0.1f ? legLength : 0.85f * scale;

            Vector3 body = animator.bodyPosition;
            float bodyHeight = Vector3.Dot(body, up) - floor;
            // Sitting on the ground, the centre of mass is about 0.3 m up, with the hip joints a hand's width up.
            float drop = Mathf.Max(0f, bodyHeight - 0.3f * scale);
            body -= up * drop * weight;
            // Lean the hips back a little so the legs reach forward from under the body.
            body -= forward * 0.06f * scale * weight;
            animator.bodyPosition = body;

            Vector3 seat = Vector3.ProjectOnPlane(body, up) + up * floor;
            foreach ((AvatarIKGoal goal, AvatarIKHint knee, float side) in new[]
                     { (AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, -1f), (AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, 1f) })
            {
                float sole = (goal == AvatarIKGoal.LeftFoot ? animator.leftFeetBottomHeight : animator.rightFeetBottomHeight) * scale;
                Vector3 foot = seat + forward * legs * 0.92f + right * side * 0.13f * scale;
                // Rest the heel on the ground where it lands (which rises or falls on a slope), clear of the toes' ground too.
                foot += up * (GroundRise(foot, scale, forward * 0.18f * scale) + sole + 0.02f * scale);
                // Heels on the ground, toes up and turned slightly out.
                Quaternion relaxed = Quaternion.LookRotation(forward, up) * Quaternion.Euler(-55f, side * 12f, 0f);
                animator.SetIKPositionWeight(goal, weight);
                animator.SetIKRotationWeight(goal, weight);
                animator.SetIKPosition(goal, foot);
                animator.SetIKRotation(goal, relaxed);
                // Knees a little bent and pointing up.
                animator.SetIKHintPositionWeight(knee, weight);
                animator.SetIKHintPosition(knee, seat + forward * legs * 0.5f + right * side * 0.14f * scale + up * 0.35f * scale);
            }

            // The sitting clip curls the toes, as if the feet were flat on the floor in front of a chair; with the
            // legs stretched out that folds the front of the foot (and shoe) down under itself.
            if (weight > 0.5f)
            {
                if (animator.GetBoneTransform(HumanBodyBones.LeftToes) != null)
                    animator.SetBoneLocalRotation(HumanBodyBones.LeftToes, leftToesRest);
                if (animator.GetBoneTransform(HumanBodyBones.RightToes) != null)
                    animator.SetBoneLocalRotation(HumanBodyBones.RightToes, rightToesRest);
            }

            foreach ((AvatarIKGoal goal, float side) in new[] { (AvatarIKGoal.LeftHand, -1f), (AvatarIKGoal.RightHand, 1f) })
            {
                // A hand holding something stays up holding it.
                if (goal == AvatarIKGoal.RightHand && HandTarget.HasValue && HandWeight > 0f)
                    continue;
                Vector3 hand = seat - forward * 0.16f * scale + right * side * 0.3f * scale;
                hand += up * (GroundRise(hand, scale, Vector3.zero) + 0.03f * scale);
                animator.SetIKPositionWeight(goal, weight * 0.85f);
                animator.SetIKRotationWeight(goal, weight * 0.6f);
                animator.SetIKPosition(goal, hand);
                // Palms flat, fingers pointing forward.
                animator.SetIKRotation(goal, Quaternion.LookRotation(forward, up));
            }
            lowerBody = 0f;
        }

        /// <summary>Moves each foot onto the ground under it and lowers the hips to match the lower foot.</summary>
        void PlantFeet(float scale)
        {
            Vector3 up = ground.up;
            float lowest = 0f;
            var targets = new (AvatarIKGoal goal, Vector3 position, Quaternion rotation, bool hit)[2];
            int i = 0;
            foreach (AvatarIKGoal goal in new[] { AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot })
            {
                Vector3 foot = animator.GetIKPosition(goal);
                float lift = Vector3.Dot(foot - ground.position, up);
                float sole = (goal == AvatarIKGoal.LeftFoot ? animator.leftFeetBottomHeight : animator.rightFeetBottomHeight) * scale;
                targets[i] = (goal, foot, animator.GetIKRotation(goal), false);
                // Only feet that are down: a foot mid-stride keeps its swing.
                if (lift < sole + 0.08f * scale && GroundBelow(foot, scale, out RaycastHit hit))
                {
                    float offset = Vector3.Dot(hit.point - ground.position, up);
                    lowest = Mathf.Min(lowest, offset);
                    Quaternion tilt = Quaternion.FromToRotation(up, hit.normal);
                    targets[i] = (goal, foot + up * offset, tilt * targets[i].rotation, true);
                }
                i++;
            }

            // Drop the hips so the downhill foot can reach, smoothly so steps don't jolt.
            lowerBody = Mathf.MoveTowards(lowerBody, Mathf.Max(lowest, -0.3f * scale), Time.deltaTime * 0.8f * scale);
            animator.bodyPosition += up * lowerBody;
            foreach ((AvatarIKGoal goal, Vector3 position, Quaternion rotation, bool hit) in targets)
            {
                float weight = hit ? 1f : 0f;
                animator.SetIKPositionWeight(goal, weight);
                animator.SetIKRotationWeight(goal, weight * 0.7f);
                animator.SetIKPosition(goal, position);
                animator.SetIKRotation(goal, rotation);
            }
        }

        /// <summary>
        /// How far the ground under a point (and under point + also) is above the hiker's own ground level, the
        /// higher of the two; 0 where there's no ground within reach.
        /// </summary>
        float GroundRise(Vector3 point, float scale, Vector3 also)
        {
            if (!GroundFeet)
                return 0f;
            float rise = float.MinValue;
            foreach (Vector3 at in new[] { point, point + also })
                if (GroundBelow(at, scale, out RaycastHit hit, 0.8f))
                    rise = Mathf.Max(rise, Vector3.Dot(hit.point - ground.position, ground.up));
            return rise == float.MinValue ? 0f : rise;
        }

        bool GroundBelow(Vector3 foot, float scale, out RaycastHit ground, float reach = 0.45f)
        {
            Vector3 from = foot + this.ground.up * reach * scale;
            int count = Physics.RaycastNonAlloc(from, -this.ground.up, hits, reach * 2f * scale, ~0, QueryTriggerInteraction.Ignore);
            ground = default;
            float nearest = float.MaxValue;
            Transform player = this.ground.root;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].transform.IsChildOf(player) || hits[i].distance >= nearest)
                    continue;
                nearest = hits[i].distance;
                ground = hits[i];
            }
            return nearest < float.MaxValue;
        }
    }
}
