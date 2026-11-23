namespace MazatrolViewerAero.Ui;

public sealed record FamilyInfo(
    string Key,
    string English,
    bool CanAdd,
    bool HasDetail,
    bool HasSubmicron,
    string ParameterFilter)
{
    public static IReadOnlyList<FamilyInfo> All { get; } =
    [
        new("M640M", "M640M Setup..", true, false, false, "M640M Machine Parameter (*.m64)|*.m64|All Files (*.*)|*.*"),
        new("M640MPro", "M640MPro Setup..", true, false, false, "M640MPro Machine Parameter (*.dba)|*.dba|All Files (*.*)|*.*"),
        new("M640T", "M640T Setup..", false, true, true, ""),
        new("M640MT", "M640MT Setup..", false, true, true, ""),
        new("M640MTPro", "M640MTPro Setup..", false, true, true, ""),
        new("MatrixIntegrex", "MATRIX( INTEGREX ) Setup", false, true, false, ""),
        new("MatrixTurning", "MATRIX( TURNING ) Setup", false, true, false, ""),
        new("MatrixMachining", "MATRIX( MACHINING ) Setup", false, true, false, ""),
        new("MatrixIntegrexE", "MATRIX( INTEGREX-e ) Setup", false, true, false, "")
    ];
}
