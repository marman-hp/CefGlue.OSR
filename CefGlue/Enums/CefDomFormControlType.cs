namespace Xilium.CefGlue
{
    /// <summary>
    /// DOM form control types. Should be kept in sync with Chromium's
    /// blink::mojom::FormControlType type.
    /// </summary>
    public enum CefDomFormControlType
    {
        Unsupported = 0,
        ButtonButton,
        ButtonSubmit,
        ButtonReset,
        ButtonPopover,
        Fieldset,
        InputButton,
        InputCheckbox,
        InputColor,
        InputDate,
        InputDatetimeLocal,
        InputEmail,
        InputFile,
        InputHidden,
        InputImage,
        InputMonth,
        InputNumber,
        InputPassword,
        InputRadio,
        InputRange,
        InputReset,
        InputSearch,
        InputSubmit,
        InputTelephone,
        InputText,
        InputTime,
        InputUrl,
        InputWeek,
        Output,
        SelectOne,
        SelectMultiple,
        TextArea,
        NumValues,
    }
}