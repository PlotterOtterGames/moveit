package al.moveit.cloud;

import android.app.job.JobParameters;
import android.app.job.JobService;

/** Background check (every ~6 h, at boot, after an update): runs without the Unity activity, so a crashing build still gets fixed. */
public final class UpdateJobService extends JobService {
    @Override
    public boolean onStartJob(final JobParameters params) {
        final TunduCloud cloud = TunduCloud.get(this);
        cloud.network.execute(() -> {
            boolean ok = cloud.sync();
            jobFinished(params, !ok && params.getJobId() == TunduCloud.JOB_NOW);
        });
        return true;
    }

    @Override
    public boolean onStopJob(JobParameters params) {
        return true; // retry later
    }
}
