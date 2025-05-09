using UnityEngine;
using UnityEngine.EventSystems;

public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler 
{
   [SerializeField] private RectTransform background;
   [SerializeField] private RectTransform handle;
   private Vector2 input = Vector2.zero;
   private bool isDragging = false;

   public Vector2 Input => input;

   public void OnPointerDown(PointerEventData eventData)
   {
       isDragging = true;
       OnDrag(eventData);
   }

   public void OnDrag(PointerEventData eventData)
   {
       if (!isDragging) return;

       Vector2 position;
       RectTransformUtility.ScreenPointToLocalPointInRectangle(
           background,
           eventData.position,
           eventData.pressEventCamera,
           out position);

       // Convert to [-1, 1] range
       position = position / (background.sizeDelta / 2);
       input = Vector2.ClampMagnitude(position, 1f);

       // Move handle
       handle.anchoredPosition = input * (background.sizeDelta / 2);
   }

   public void OnPointerUp(PointerEventData eventData)
   {
       isDragging = false;
       input = Vector2.zero;
       handle.anchoredPosition = Vector2.zero;
   }
}