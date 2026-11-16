using System.Windows;

namespace N_Remo
{
    public static class ButtonEffects
    {
        public static readonly DependencyProperty
            IsMouseOverEffectEnabledProperty =
                DependencyProperty.RegisterAttached(
                    "IsMouseOverEffectEnabled",
                    typeof(bool),
                    typeof(ButtonEffects),
                    new PropertyMetadata(true));

        public static bool GetIsMouseOverEffectEnabled(
            DependencyObject element)
        {
            return (bool)element.GetValue(
                IsMouseOverEffectEnabledProperty);
        }

        public static void SetIsMouseOverEffectEnabled(
            DependencyObject element,
            bool value)
        {
            element.SetValue(
                IsMouseOverEffectEnabledProperty,
                value);
        }
    }
}