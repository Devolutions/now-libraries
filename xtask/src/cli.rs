const HELP: &str = "\
cargo xtask

USAGE:
  cargo xtask [OPTIONS] [TASK]

FLAGS:
  -h, --help      Prints help information
  -v, --verbose   Prints additional execution traces

TASKS:
  bootstrap               Install all requirements for development
  check install           Install all requirements for check tasks
  check locks             Check for dirty or staged lock files not yet committed
  check typos             Check for typos in the codebase
  ci                      Run all checks required on CI
  clean                   Clean workspace
  dotnet build            Build the .NET packages
  dotnet fmt              Check formatting for .NET packages
  dotnet tests            Compile .NET tests and run them
  rust fmt               Check formatting for Rust crates
  rust lints             Check lints
  rust tests [--no-run]  Compile Rust tests and, unless specified otherwise, run them
  unicode tables         Download the Unicode data files and regenerate the policy evaluator tables
";

pub fn print_help() {
    println!("{HELP}");
}

pub struct Args {
    pub verbose: bool,
    pub action: Action,
}

pub enum Action {
    Bootstrap,
    CheckInstall,
    CheckLocks,
    CheckTypos,
    Ci,
    Clean,
    DotnetFmt,
    DotnetBuild,
    DotnetTests,
    RustFmt,
    RustLints,
    RustTests { no_run: bool },
    UnicodeTables,
    ShowHelp,
}

pub fn parse_args() -> anyhow::Result<Args> {
    let mut args = pico_args::Arguments::from_env();

    let action = if args.contains(["-h", "--help"]) {
        Action::ShowHelp
    } else {
        match args.subcommand()?.as_deref() {
            Some("bootstrap") => Action::Bootstrap,
            Some("check") => match args.subcommand()?.as_deref() {
                Some("locks") => Action::CheckLocks,
                Some("typos") => Action::CheckTypos,
                Some("install") => Action::CheckInstall,
                Some(unknown) => anyhow::bail!("unknown check action: {unknown}"),
                None => Action::ShowHelp,
            },
            Some("rust") => match args.subcommand()?.as_deref() {
                Some("fmt") => Action::RustFmt,
                Some("lints") => Action::RustLints,
                Some("tests") => Action::RustTests {
                    no_run: args.contains("--no-run"),
                },
                Some(unknown) => anyhow::bail!("unknown check action: {unknown}"),
                None => Action::ShowHelp,
            },
            Some("dotnet") => match args.subcommand()?.as_deref() {
                Some("fmt") => Action::DotnetFmt,
                Some("build") => Action::DotnetBuild,
                Some("tests") => Action::DotnetTests,
                Some(unknown) => anyhow::bail!("unknown check action: {unknown}"),
                None => Action::ShowHelp,
            },
            Some("unicode") => match args.subcommand()?.as_deref() {
                Some("tables") => Action::UnicodeTables,
                Some(unknown) => anyhow::bail!("unknown unicode action: {unknown}"),
                None => Action::ShowHelp,
            },
            Some("ci") => Action::Ci,
            Some("clean") => Action::Clean,
            None | Some(_) => Action::ShowHelp,
        }
    };

    let verbose = args.contains(["-v", "--verbose"]);

    Ok(Args { verbose, action })
}
