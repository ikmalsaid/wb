using System;

namespace wb.AdBlock
{
    /// <summary>
    /// Interface allowing element picker UI components (forms/panels) to interact
    /// with the underlying browser tab or picker host without tight coupling.
    /// </summary>
    public interface IElementPickerTarget
    {
        void SetElementPickerPreview(bool preview, string selector);
        void ResumeElementPicker();
        void CancelElementPicker();
        void CommitElementPickerBlock(string selector);
    }
}
