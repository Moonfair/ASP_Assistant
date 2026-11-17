#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char **argv) {
    (void)argc;
    const char *home = getenv("HOME");
    if (home && !getenv("DOTNET_ROOT")) {
        char dotnet[PATH_MAX];
        int length = snprintf(dotnet, sizeof(dotnet), "%s/.dotnet/dotnet", home);
        if (length > 0 && (size_t)length < sizeof(dotnet) &&
            access(dotnet, X_OK) == 0) {
            char root[PATH_MAX];
            snprintf(root, sizeof(root), "%s/.dotnet", home);
            setenv("DOTNET_ROOT", root, 1);
        }
    }

    char executable[PATH_MAX];
    if (!realpath(argv[0], executable)) {
        perror("ASP Assistant launcher");
        return 1;
    }
    char *slash = strrchr(executable, '/');
    if (!slash) return 1;
    strcpy(slash + 1, "ASPAssistant.Mac");
    argv[0] = executable;
    execv(executable, argv);
    perror("ASP Assistant launcher");
    return 1;
}
