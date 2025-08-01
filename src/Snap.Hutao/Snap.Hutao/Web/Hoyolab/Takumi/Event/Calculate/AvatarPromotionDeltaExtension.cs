// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.ViewModel.AvatarProperty;

namespace Snap.Hutao.Web.Hoyolab.Takumi.Event.Calculate;

internal static class AvatarPromotionDeltaExtension
{
    public static bool TryGetNonErrorCopy(this AvatarPromotionDelta source, AvatarView avatar, [NotNullWhen(true)] out AvatarPromotionDelta? copy)
    {
        copy = new()
        {
            AvatarId = avatar.Id,
            AvatarLevelCurrent = Math.Min(avatar.LevelNumber, source.AvatarLevelTarget),
            AvatarLevelTarget = source.AvatarLevelTarget,
            AvatarPromoteLevel = avatar.PromoteLevel,
        };

        if (avatar.Skills is not [{ } skillViewA, { } skillViewE, { } skillViewQ, ..] ||
            source.SkillList is not [{ } deltaA, { } deltaE, { } deltaQ, ..])
        {
            return false;
        }

        copy.SkillList =
        [
            new()
            {
                Id = skillViewA.GroupId,
                LevelCurrent = Math.Min(skillViewA.LevelNumber, deltaA.LevelTarget),
                LevelTarget = deltaA.LevelTarget,
            },
            new()
            {
                Id = skillViewE.GroupId,
                LevelCurrent = Math.Min(skillViewE.LevelNumber, deltaE.LevelTarget),
                LevelTarget = deltaE.LevelTarget,
            },
            new()
            {
                Id = skillViewQ.GroupId,
                LevelCurrent = Math.Min(skillViewQ.LevelNumber, deltaQ.LevelTarget),
                LevelTarget = deltaQ.LevelTarget,
            },
        ];

        if (avatar.Weapon is not { } weaponView || source.Weapon is not { } deltaWeapon)
        {
            return false;
        }

        copy.Weapon = new()
        {
            Id = weaponView.Id,
            LevelCurrent = Math.Min(weaponView.LevelNumber, deltaWeapon.LevelTarget),
            LevelTarget = Math.Min(weaponView.MaxLevel, deltaWeapon.LevelTarget),
            WeaponPromoteLevel = weaponView.PromoteLevel,
        };

        return true;
    }
}