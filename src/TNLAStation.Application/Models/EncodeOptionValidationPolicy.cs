namespace TNLAStation.Application.Models;

/// <summary>
/// エンコードオプションの検査。上流の ReserveOptionChecker.checkEncodeOption はルールの
/// 追加・更新 (<see cref="RuleValidationPolicy"/>) と手動予約の追加・編集の両方から同じ
/// ロジックで呼ばれているので、ここに1本化する。
/// </summary>
public static class EncodeOptionValidationPolicy
{
    public static bool IsValid(
        ReserveEncodeSettings? option,
        IReadOnlyCollection<string> encodeModeNames,
        bool hasEncodeConfig)
    {
        if (option is null)
        {
            return true;
        }

        if (!hasEncodeConfig)
        {
            return false;
        }

        if (option.Mode1 is not null && !encodeModeNames.Contains(option.Mode1))
        {
            return false;
        }

        if (option.Mode2 is not null && !encodeModeNames.Contains(option.Mode2))
        {
            return false;
        }

        if (option.Mode3 is not null && !encodeModeNames.Contains(option.Mode3))
        {
            return false;
        }

        if (option.Mode1 is null && option.Directory1 is not null)
        {
            return false;
        }

        if (option.Mode2 is null && option.Directory2 is not null)
        {
            return false;
        }

        return option.Mode3 is not null || option.Directory3 is null;
    }
}
