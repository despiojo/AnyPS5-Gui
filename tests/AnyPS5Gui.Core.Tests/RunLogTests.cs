using AnyPS5Gui.Core.Logs;

namespace AnyPS5Gui.Core.Tests;

public class RunLogTests
{
    private static readonly DateTime Noon = new(2026, 11, 16, 12, 0, 0);

    [Fact]
    public void A_run_writes_its_lines_to_a_file_named_after_it()
    {
        using var temp = new TempDirectory();
        var directory = temp.Combine("logs");

        string path;
        using (var log = RunLog.Start(directory, "run PPSA01234", keep: 10, Noon)!)
        {
            path = log.Path;
            log.Write("first");
            log.Write("second");
        }

        Assert.Equal(Path.Combine(directory, "20261116-120000-run-PPSA01234.log"), path);
        Assert.Equal(["first", "second"], File.ReadAllLines(path));
    }

    [Fact]
    public void The_file_can_be_read_while_the_run_goes_on()
    {
        using var temp = new TempDirectory();
        using var log = RunLog.Start(temp.Path, "run", keep: 10, Noon)!;
        log.Write("so far");

        using var reader = new StreamReader(new FileStream(log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        Assert.Equal("so far", reader.ReadLine());
    }

    [Fact]
    public void A_progress_line_redrawn_in_place_is_written_once_with_its_last_value()
    {
        using var temp = new TempDirectory();
        string path;
        using (var log = RunLog.Start(temp.Path, "convert", keep: 10, Noon)!)
        {
            path = log.Path;
            log.Write("Converting eboot.bin");
            log.Write("Scan | eboot.bin |  10%", "Scan | eboot.bin ");
            log.Write("Scan | eboot.bin |  60%", "Scan | eboot.bin ");
            log.Write("Scan | eboot.bin | 100%", "Scan | eboot.bin ");
            log.Write("Scan | libc.prx | 100%", "Scan | libc.prx ");
            log.Write("done");
            log.Write("Write | out.exe |  50%", "Write | out.exe ");
        }

        Assert.Equal(
            ["Converting eboot.bin", "Scan | eboot.bin | 100%", "Scan | libc.prx | 100%", "done", "Write | out.exe |  50%"],
            File.ReadAllLines(path));
    }

    [Fact]
    public void Starting_a_run_deletes_all_but_the_newest_logs()
    {
        using var temp = new TempDirectory();
        for (var hour = 1; hour <= 5; hour++) temp.Write($"20261116-0{hour}0000-run.log");
        temp.Write("notes.txt");

        using var log = RunLog.Start(temp.Path, "run", keep: 3, Noon)!;

        Assert.Equal(
            ["20261116-040000-run.log", "20261116-050000-run.log", "20261116-120000-run.log", "notes.txt"],
            Directory.GetFiles(temp.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Keeping_none_writes_no_file_and_removes_the_old_ones()
    {
        using var temp = new TempDirectory();
        temp.Write("20261116-010000-run.log");

        Assert.Null(RunLog.Start(temp.Path, "run", keep: 0, Noon));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Two_runs_in_the_same_second_get_different_files()
    {
        using var temp = new TempDirectory();
        using var first = RunLog.Start(temp.Path, "run", keep: 10, Noon)!;
        using var second = RunLog.Start(temp.Path, "run", keep: 10, Noon)!;

        Assert.NotEqual(first.Path, second.Path);
        Assert.True(File.Exists(second.Path));
    }

    [Fact]
    public void A_name_that_is_not_a_file_name_is_made_into_one()
    {
        using var temp = new TempDirectory();
        using var log = RunLog.Start(temp.Path, "run Ghost: of \"Y\"?", keep: 10, Noon)!;

        Assert.Equal("20261116-120000-run-Ghost-of-Y.log", Path.GetFileName(log.Path));
    }

    [Fact]
    public void Lines_written_after_the_run_ended_are_ignored()
    {
        using var temp = new TempDirectory();
        var log = RunLog.Start(temp.Path, "run", keep: 10, Noon)!;
        log.Write("kept");
        log.Dispose();
        log.Write("late");

        Assert.Equal(["kept"], File.ReadAllLines(log.Path));
    }
}
