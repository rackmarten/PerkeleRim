// Writes PerkeleRim's empty Scaleform movie (protocol/cpp/swf.h) to the path given, at build time.

#include <cstdio>

#include "swf.h"

int main(int a_argc, char** a_argv)
{
    if (a_argc != 2) {
        std::fprintf(stderr, "usage: swfgen <out.swf>\n");
        return 2;
    }
    const auto movie = PerkeleRim::Swf::EmptyMovie();
    std::FILE* file = std::fopen(a_argv[1], "wb");
    if (!file) {
        std::perror(a_argv[1]);
        return 1;
    }
    const bool ok = std::fwrite(movie.data(), 1, movie.size(), file) == movie.size();
    return std::fclose(file) == 0 && ok ? 0 : 1;
}
