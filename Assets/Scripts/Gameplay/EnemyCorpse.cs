using UnityEngine;

namespace Parry
{
    public sealed class EnemyCorpse : MonoBehaviour
    {
        SpriteRenderer body;
        Camera view;
        float fallAngle;

        public static EnemyCorpse Create(SpriteRenderer source, Color color, Vector3 position, Vector3 direction, Transform arena)
        {
            var remains = new GameObject("Fallen enemy");
            remains.transform.SetParent(arena, false);
            remains.transform.position = position;
            var corpse = remains.AddComponent<EnemyCorpse>();
            corpse.view = Camera.main;
            corpse.fallAngle = Vector3.Dot(direction, corpse.view.transform.right) >= 0 ? -90 : 90;
            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(remains.transform, false);
            bodyObject.transform.localPosition = Vector3.up * .32f;
            bodyObject.transform.localScale = new Vector3(.45f, .9f, 1);
            corpse.body = bodyObject.AddComponent<SpriteRenderer>();
            corpse.body.sprite = source.sprite;
            corpse.body.sharedMaterial = source.sharedMaterial;
            corpse.body.flipX = source.flipX;
            corpse.body.color = Color.Lerp(color, new Color(.18f, .13f, .16f), .5f);
            DemoArt.Shadow(remains.transform);
            corpse.LateUpdate();
            return corpse;
        }

        void LateUpdate()
        {
            body.transform.rotation = view.transform.rotation * Quaternion.Euler(0, 0, fallAngle);
        }
    }
}
