#define _POSIX_C_SOURCE 200809L

#include <errno.h>
#include <pthread.h>
#include <semaphore.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct {
    pthread_mutex_t mutex;
    sem_t hydrogen_queue;
    sem_t oxygen_queue;
    size_t waiting_hydrogen;
    size_t waiting_oxygen;
    size_t bonded;
    size_t result_length;
    size_t result_position;
    bool molecule_active;
    char *result;
} Coordinator;

typedef struct {
    Coordinator *coordinator;
    char atom;
} Atom;

static void check_pthread(int error, const char *operation)
{
    if (error != 0) {
        fprintf(stderr, "%s: %s\n", operation, strerror(error));
        exit(EXIT_FAILURE);
    }
}

static void check_semaphore(int result, const char *operation)
{
    if (result == -1) {
        perror(operation);
        exit(EXIT_FAILURE);
    }
}

static void wait_semaphore(sem_t *semaphore)
{
    while (sem_wait(semaphore) == -1) {
        if (errno != EINTR) {
            perror("sem_wait");
            exit(EXIT_FAILURE);
        }
    }
}

/* Called with the coordinator mutex held. */
static void release_molecule_if_ready(Coordinator *coordinator)
{
    if (coordinator->molecule_active ||
        coordinator->waiting_hydrogen < 2 ||
        coordinator->waiting_oxygen < 1) {
        return;
    }

    coordinator->molecule_active = true;
    coordinator->waiting_hydrogen -= 2;
    coordinator->waiting_oxygen--;
    coordinator->bonded = 0;

    check_semaphore(sem_post(&coordinator->hydrogen_queue), "sem_post hydrogen");
    check_semaphore(sem_post(&coordinator->hydrogen_queue), "sem_post hydrogen");
    check_semaphore(sem_post(&coordinator->oxygen_queue), "sem_post oxygen");
}

static void bond(Coordinator *coordinator, char atom)
{
    check_pthread(pthread_mutex_lock(&coordinator->mutex), "pthread_mutex_lock");
    coordinator->result[coordinator->result_position++] = atom;
    check_pthread(pthread_mutex_unlock(&coordinator->mutex), "pthread_mutex_unlock");
}

static void *atom_thread(void *argument)
{
    Atom *atom = argument;
    Coordinator *coordinator = atom->coordinator;

    check_pthread(pthread_mutex_lock(&coordinator->mutex), "pthread_mutex_lock");
    if (atom->atom == 'H') {
        coordinator->waiting_hydrogen++;
    } else {
        coordinator->waiting_oxygen++;
    }
    release_molecule_if_ready(coordinator);
    check_pthread(pthread_mutex_unlock(&coordinator->mutex), "pthread_mutex_unlock");

    wait_semaphore(atom->atom == 'H'
        ? &coordinator->hydrogen_queue
        : &coordinator->oxygen_queue);

    bond(coordinator, atom->atom);

    check_pthread(pthread_mutex_lock(&coordinator->mutex), "pthread_mutex_lock");
    coordinator->bonded++;
    if (coordinator->bonded == 3) {
        coordinator->molecule_active = false;
        release_molecule_if_ready(coordinator);
    }
    check_pthread(pthread_mutex_unlock(&coordinator->mutex), "pthread_mutex_unlock");
    return NULL;
}

static bool validate_input(const char *input, size_t *molecule_count)
{
    size_t hydrogen_count = 0;
    size_t oxygen_count = 0;
    size_t atom_count = strlen(input);

    if (atom_count == 0 || atom_count % 3 != 0) {
        return false;
    }

    for (size_t i = 0; i < atom_count; i++) {
        if (input[i] == 'H') {
            hydrogen_count++;
        } else if (input[i] == 'O') {
            oxygen_count++;
        } else {
            return false;
        }
    }

    if (hydrogen_count != atom_count / 3 * 2 ||
        oxygen_count != atom_count / 3) {
        return false;
    }

    *molecule_count = atom_count / 3;
    return true;
}

static bool validate_result(const char *result, size_t atom_count)
{
    for (size_t start = 0; start < atom_count; start += 3) {
        size_t hydrogen_count = 0;
        size_t oxygen_count = 0;

        for (size_t offset = 0; offset < 3; offset++) {
            if (result[start + offset] == 'H') {
                hydrogen_count++;
            } else if (result[start + offset] == 'O') {
                oxygen_count++;
            }
        }

        if (hydrogen_count != 2 || oxygen_count != 1) {
            return false;
        }
    }
    return true;
}

static void print_usage(const char *program)
{
    fprintf(stderr, "Uso: %s SEQUENCIA_DE_ATOMOS\n", program);
    fprintf(stderr, "Exemplos: %s HOH | %s OOHHHH\n", program, program);
}

int main(int argc, char **argv)
{
    if (argc != 2) {
        print_usage(argv[0]);
        return EXIT_FAILURE;
    }

    const char *input = argv[1];
    size_t molecule_count = 0;
    if (!validate_input(input, &molecule_count)) {
        fprintf(stderr, "Entrada invalida: use apenas H e O, com dois H para cada O.\n");
        print_usage(argv[0]);
        return EXIT_FAILURE;
    }

    size_t atom_count = strlen(input);
    pthread_t *threads = calloc(atom_count, sizeof(*threads));
    Atom *atoms = calloc(atom_count, sizeof(*atoms));
    char *result = calloc(atom_count + 1, sizeof(*result));
    if (threads == NULL || atoms == NULL || result == NULL) {
        perror("calloc");
        free(threads);
        free(atoms);
        free(result);
        return EXIT_FAILURE;
    }

    Coordinator coordinator = {
        .result_length = atom_count,
        .result = result,
    };

    check_pthread(pthread_mutex_init(&coordinator.mutex, NULL), "pthread_mutex_init");
    check_semaphore(sem_init(&coordinator.hydrogen_queue, 0, 0), "sem_init hydrogen");
    check_semaphore(sem_init(&coordinator.oxygen_queue, 0, 0), "sem_init oxygen");

    for (size_t i = 0; i < atom_count; i++) {
        atoms[i].coordinator = &coordinator;
        atoms[i].atom = input[i];
        int error = pthread_create(&threads[i], NULL, atom_thread, &atoms[i]);
        if (error != 0) {
            fprintf(stderr, "pthread_create: %s\n", strerror(error));
            exit(EXIT_FAILURE);
        }
    }

    for (size_t i = 0; i < atom_count; i++) {
        check_pthread(pthread_join(threads[i], NULL), "pthread_join");
    }

    result[coordinator.result_position] = '\0';
    bool valid = coordinator.result_position == coordinator.result_length &&
        validate_result(result, atom_count);

    if (valid) {
        printf("Chegada: %s\n", input);
        printf("Ligacoes (ordem de bond): %s\n", result);
        printf("OK: %zu molecula(s) formada(s), cada grupo contem 2 H e 1 O.\n",
            molecule_count);
    } else {
        fprintf(stderr, "Falha: a ordem de ligacao nao formou moleculas validas.\n");
    }

    check_semaphore(sem_destroy(&coordinator.hydrogen_queue), "sem_destroy hydrogen");
    check_semaphore(sem_destroy(&coordinator.oxygen_queue), "sem_destroy oxygen");
    check_pthread(pthread_mutex_destroy(&coordinator.mutex), "pthread_mutex_destroy");
    free(threads);
    free(atoms);
    free(result);
    return valid ? EXIT_SUCCESS : EXIT_FAILURE;
}
