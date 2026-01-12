cmake_minimum_required(VERSION 3.16)
project(linesimplify CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_library(linesimplify SHARED linesimplify.cpp)

# Make symbol visibility sane
if (UNIX)
  target_compile_options(linesimplify PRIVATE -fvisibility=hidden)
endif()

# Export our C API symbol
target_compile_definitions(linesimplify PRIVATE LINESIMPLIFY_EXPORTS=1)
