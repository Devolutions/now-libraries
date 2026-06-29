//! Compatibility conversions for the `now-policy` crate.

use super::{
    Architecture, CustomParameterString, Decision, Elevation, ManagerName, Operation, ResourceId, RuleId, Scope,
    SemanticVersion, VersionString,
};

macro_rules! bidirectional_enum_conversion {
    ($local:ty, $policy:ty, [$($variant:ident),+ $(,)?]) => {
        impl From<$policy> for $local {
            fn from(value: $policy) -> Self {
                match value {
                    $(<$policy>::$variant => Self::$variant,)+
                }
            }
        }

        impl From<$local> for $policy {
            fn from(value: $local) -> Self {
                match value {
                    $(<$local>::$variant => Self::$variant,)+
                }
            }
        }
    };
}

macro_rules! bidirectional_newtype_conversion {
    ($local:ty, $policy:ty) => {
        impl From<$policy> for $local {
            fn from(value: $policy) -> Self {
                Self(value.0)
            }
        }

        impl From<$local> for $policy {
            fn from(value: $local) -> Self {
                Self(value.0)
            }
        }
    };
}

bidirectional_enum_conversion!(Operation, now_policy::Operation, [Install, Update, Uninstall]);
bidirectional_enum_conversion!(Scope, now_policy::Scope, [User, Machine]);
bidirectional_enum_conversion!(Architecture, now_policy::Architecture, [X86, X64, Arm64, Neutral]);
bidirectional_enum_conversion!(ManagerName, now_policy::ManagerName, [Winget, PowerShell, PowerShell7]);
bidirectional_enum_conversion!(Decision, now_policy::Decision, [Allow, Deny]);
bidirectional_enum_conversion!(Elevation, now_policy::Elevation, [Standard, Elevated]);

bidirectional_newtype_conversion!(ResourceId, now_policy::ResourceId);
bidirectional_newtype_conversion!(SemanticVersion, now_policy::SemanticVersion);
bidirectional_newtype_conversion!(VersionString, now_policy::VersionString);
bidirectional_newtype_conversion!(CustomParameterString, now_policy::CustomParameterString);

impl From<now_policy::ResourceId> for RuleId {
    fn from(value: now_policy::ResourceId) -> Self {
        Self(value.0)
    }
}

impl TryFrom<RuleId> for now_policy::ResourceId {
    type Error = now_policy::ModelValidationError;

    fn try_from(value: RuleId) -> Result<Self, Self::Error> {
        now_policy::ResourceId::parse(&value.0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn policy_enum_conversions_round_trip() {
        assert_eq!(now_policy::Operation::Install, Operation::Install.into());
        assert_eq!(Operation::Update, now_policy::Operation::Update.into());

        assert_eq!(now_policy::Scope::Machine, Scope::Machine.into());
        assert_eq!(Scope::User, now_policy::Scope::User.into());

        assert_eq!(now_policy::Architecture::Arm64, Architecture::Arm64.into());
        assert_eq!(Architecture::Neutral, now_policy::Architecture::Neutral.into());

        assert_eq!(now_policy::ManagerName::PowerShell7, ManagerName::PowerShell7.into());
        assert_eq!(ManagerName::Winget, now_policy::ManagerName::Winget.into());

        assert_eq!(now_policy::Decision::Deny, Decision::Deny.into());
        assert_eq!(Decision::Allow, now_policy::Decision::Allow.into());

        assert_eq!(now_policy::Elevation::Elevated, Elevation::Elevated.into());
        assert_eq!(Elevation::Standard, now_policy::Elevation::Standard.into());
    }

    #[test]
    fn policy_newtype_conversions_round_trip() {
        let policy_resource = now_policy::ResourceId::parse("policy:rule-1").expect("valid policy resource id");
        let broker_resource = ResourceId::from(policy_resource.clone());
        assert_eq!("policy:rule-1", broker_resource.as_ref());
        assert_eq!(policy_resource, broker_resource.into());

        let policy_version = now_policy::SemanticVersion::parse("1.2.3").expect("valid semantic version");
        let broker_version = SemanticVersion::from(policy_version.clone());
        assert_eq!("1.2.3", broker_version.as_ref());
        assert_eq!(policy_version, broker_version.into());

        let policy_package_version = now_policy::VersionString::parse("2.0.0-preview").expect("valid package version");
        let broker_package_version = VersionString::from(policy_package_version.clone());
        assert_eq!("2.0.0-preview", broker_package_version.as_ref());
        assert_eq!(policy_package_version, broker_package_version.into());

        let policy_parameters = now_policy::CustomParameterString::parse("--silent").expect("valid custom parameters");
        let broker_parameters = CustomParameterString::from(policy_parameters.clone());
        assert_eq!("--silent", broker_parameters.as_ref());
        assert_eq!(policy_parameters, broker_parameters.into());
    }

    #[test]
    fn rule_id_converts_from_policy_resource_id() {
        let policy_rule_id = now_policy::ResourceId::parse("rule-1").expect("valid policy rule id");
        let broker_rule_id = RuleId::from(policy_rule_id.clone());
        assert_eq!("rule-1", broker_rule_id.as_ref());
        assert_eq!(
            policy_rule_id,
            broker_rule_id
                .try_into()
                .expect("broker rule id converts back to policy id")
        );
    }
}
