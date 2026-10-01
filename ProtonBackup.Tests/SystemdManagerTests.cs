using ProtonBackup.Core;

namespace ProtonBackup.Tests;

public class SystemdManagerTests
{
    private const string Timer = "protonbackup-sync.timer";

    [Fact]
    public void NextRunIsReadFromTheRealListTimersFormat()
    {
        // Captured from the real systemctl: columns are separated by single spaces here.
        const string output = """
            NEXT                         LEFT LAST PASSED UNIT                     ACTIVATES
            Thu 2026-10-01 08:47:17 CEST 9min -         - protonbackup-sync.timer protonbackup-sync.service

            1 timers listed.
            Pass --all to see loaded but inactive timers, too.
            """;
        Assert.Equal("Thu 2026-10-01 08:47:17 CEST", SystemdManager.ParseNextRun(output, Timer));
    }

    [Fact]
    public void NextRunWorksWhenTheLeftColumnIsWider() =>
        Assert.Equal("Thu 2026-10-01 10:47:17 CEST", SystemdManager.ParseNextRun(
            "NEXT                         LEFT          LAST PASSED UNIT                      ACTIVATES\n" +
            "Thu 2026-10-01 10:47:17 CEST 1h 59min left -    -      protonbackup-sync.timer   protonbackup-sync.service\n", Timer));

    [Fact]
    public void NextRunIsNullWhenTheTimerIsNotListedOrNotScheduled()
    {
        Assert.Null(SystemdManager.ParseNextRun("NEXT LEFT LAST PASSED UNIT ACTIVATES\n\n0 timers listed.\n", Timer));
        Assert.Null(SystemdManager.ParseNextRun("", Timer));
        Assert.Null(SystemdManager.ParseNextRun("-      -    -    -      protonbackup-sync.timer protonbackup-sync.service\n", Timer));
    }

    [Fact]
    public void NextRunOfAnotherTimerIsIgnored() =>
        Assert.Null(SystemdManager.ParseNextRun("Thu 2026-10-01 08:47:17 CEST 9min -  - other.timer other.service\n", Timer));

    [Fact]
    public void IntervalsAreFormattedInWholeMinutesOrSeconds()
    {
        Assert.Equal("2min", SystemdManager.FormatInterval(TimeSpan.FromMinutes(2)));
        Assert.Equal("30min", SystemdManager.FormatInterval(TimeSpan.FromMinutes(30)));
        Assert.Equal("90s", SystemdManager.FormatInterval(TimeSpan.FromSeconds(90)));
        Assert.Equal("30s", SystemdManager.FormatInterval(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void AnExecutablePathWithSpacesOrPercentSignsIsEscapedForSystemd()
    {
        Assert.Equal("/usr/bin/protonbackup", SystemdManager.ExecStartCommand("/usr/bin/protonbackup"));
        Assert.Equal("\"/opt/My Apps/50%%/protonbackup\"", SystemdManager.ExecStartCommand("/opt/My Apps/50%/protonbackup"));
    }
}
