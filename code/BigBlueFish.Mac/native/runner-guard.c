/* macOS has no PR_SET_PDEATHSIG, so the guard forks the helper (FFmpeg or
   llama-server) and watches its own parent with kqueue: when BigBlueFish exits
   for any reason, including a crash, the helper is killed instead of being left
   running. The helper inherits the guard's standard streams, and the guard
   exits with the helper's status. */
#include <errno.h>
#include <signal.h>
#include <stdio.h>
#include <sys/event.h>
#include <sys/wait.h>
#include <unistd.h>

static volatile pid_t child = -1;
static void forward(int signal) { if (child > 0) kill(child, signal); }

int main(int argc, char **argv) {
    if (argc < 2) return 64;
    pid_t parent = getppid();
    int queue = kqueue();
    if (queue < 0) return 70;
    struct kevent watch;
    EV_SET(&watch, parent, EVFILT_PROC, EV_ADD | EV_ONESHOT, NOTE_EXIT, 0, NULL);
    if (kevent(queue, &watch, 1, NULL, 0, NULL) != 0 || getppid() != parent) return 70;
    child = fork();
    if (child < 0) return 70;
    if (child == 0) {
        close(queue);
        execvp(argv[1], argv + 1);
        perror("BigBlueFish runner");
        _exit(71);
    }
    signal(SIGTERM, forward);
    signal(SIGINT, forward);
    signal(SIGHUP, forward);
    /* Registration fails only if the helper is already gone; waitpid reports it. */
    EV_SET(&watch, child, EVFILT_PROC, EV_ADD | EV_ONESHOT, NOTE_EXIT, 0, NULL);
    kevent(queue, &watch, 1, NULL, 0, NULL);
    for (;;) {
        int status = 0;
        if (waitpid(child, &status, WNOHANG) == child)
            return WIFEXITED(status) ? WEXITSTATUS(status) : 128 + WTERMSIG(status);
        struct kevent event;
        int count = kevent(queue, NULL, 0, &event, 1, NULL);
        if (count < 0 && errno == EINTR) continue;
        if (count < 0 || (count == 1 && (pid_t)event.ident == parent)) {
            kill(child, SIGKILL);
            waitpid(child, &status, 0);
            return 137;
        }
    }
}
