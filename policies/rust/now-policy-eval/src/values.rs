//! Closed sets of values that policies and requests share.
//!
//! Every value matches only with its exact characters ([§text-comparison]).
//!
//! [§text-comparison]: ../../../CONTRACT.policy.md#text-comparison

macro_rules! closed_set {
    ($(#[$meta:meta])* $vis:vis enum $name:ident { $($variant:ident),+ $(,)? }) => {
        $(#[$meta])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
        $vis enum $name {
            $($variant),+
        }

        impl $name {
            /// Every value, in the order the contract lists them.
            #[allow(dead_code, reason = "not every set is iterated")]
            $vis const ALL: &[Self] = &[$(Self::$variant),+];

            /// Returns the value spelled exactly `name`.
            #[allow(dead_code, reason = "not every set is parsed from text")]
            $vis fn from_name(name: &str) -> Option<Self> {
                match name {
                    $(stringify!($variant) => Some(Self::$variant),)+
                    _ => None,
                }
            }

            /// Returns the contract's name for the value.
            #[allow(dead_code, reason = "not every set is named back")]
            $vis fn as_str(self) -> &'static str {
                match self {
                    $(Self::$variant => stringify!($variant),)+
                }
            }
        }
    };
}

closed_set! {
    pub(crate) enum Operation { Install, Update, Uninstall }
}

closed_set! {
    pub(crate) enum Manager {
        Winget,
        PowerShell,
        PowerShell7,
        Apt,
        Bun,
        Cargo,
        Chocolatey,
        Dnf,
        Dotnet,
        Flatpak,
        Homebrew,
        Npm,
        Pacman,
        Pip,
        Scoop,
        Snap,
        Vcpkg,
    }
}

closed_set! {
    pub(crate) enum Scope { User, Machine }
}

closed_set! {
    pub(crate) enum Architecture { X86, X64, Arm64, Neutral }
}

closed_set! {
    pub(crate) enum Elevation { NonElevated, Elevated }
}

closed_set! {
    pub(crate) enum RequestedElevation { Standard, Elevated }
}

closed_set! {
    pub(crate) enum RuleDecision { Allow, Deny }
}

closed_set! {
    /// An opt-in ([§opt-ins]).
    ///
    /// The order of the variants is the order in which a decision names opt-ins.
    ///
    /// [§opt-ins]: ../../../CONTRACT.policy.md#opt-ins
    pub enum OptIn {
        Interactive,
        SkipHashCheck,
        CustomInstallLocation,
        CustomParameters,
        InstallerArguments,
        PrePostCommands,
        KillBeforeOperation,
        UninstallPrevious,
        NoUpgrade,
    }
}

impl OptIn {
    /// Returns the position of the opt-in in [`OptIn::ALL`].
    pub(crate) fn index(self) -> usize {
        self as usize
    }

    /// Returns whether a grant or forbid of this opt-in has `Patterns` and `Except` ([§grants]).
    ///
    /// [§grants]: ../../../CONTRACT.policy.md#grants
    pub(crate) fn has_patterns(self) -> bool {
        matches!(self, Self::CustomParameters | Self::CustomInstallLocation)
    }
}

/// A set of opt-ins.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub(crate) struct OptInSet([bool; OptIn::ALL.len()]);

impl OptInSet {
    pub(crate) fn insert(&mut self, opt_in: OptIn) {
        self.0[opt_in.index()] = true;
    }

    pub(crate) fn contains(&self, opt_in: OptIn) -> bool {
        self.0[opt_in.index()]
    }

    /// Returns the opt-ins in the set, in the order a decision names them.
    pub(crate) fn iter(&self) -> impl Iterator<Item = OptIn> + '_ {
        OptIn::ALL.iter().copied().filter(|opt_in| self.contains(*opt_in))
    }
}
