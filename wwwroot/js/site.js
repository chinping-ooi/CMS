// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
window.formValidation = {
	setFieldError: function (input, message) {
		var $input = $(input);
		var $feedback = $input.parent().find('.invalid-feedback');
		$feedback.text(message);
		$input.addClass('is-invalid');
	},

	clearFieldError: function (input) {
		var $input = $(input);
		var $feedback = $input.parent().find('.invalid-feedback');
		$feedback.text('');
		$input.removeClass('is-invalid');
	},

	validateField: function (input) {
		var $input = $(input);
		var value = $input.val().trim();
		var isEmail = $input.attr('type') === 'email';
		var requiredMessage = $input.attr('data-validation-message') || 'This field is required.';

		if (!value) {
			this.setFieldError(input, requiredMessage);
			return false;
		}

		if (isEmail) {
			var atIndex = value.indexOf(String.fromCharCode(64));
			var dotAfterAtIndex = value.indexOf('.', atIndex + 2);
			if (atIndex < 1 || value.lastIndexOf(String.fromCharCode(64)) !== atIndex || dotAfterAtIndex === -1 || dotAfterAtIndex === value.length - 1) {
				this.setFieldError(input, 'Please enter a valid email address.');
				return false;
			}
		}

		this.clearFieldError(input);
		return true;
	},

	setup: function (formId) {
		var $form = $('#' + formId);
		if (!$form.length) return;

		$form.find('input[required], select[required], textarea[required]').each(function () {
			var $input = $(this);
			$input.on('input blur', function () {
				window.formValidation.validateField(this);
			});
		});

		$form.on('submit', function (event) {
			var isValid = true;
			$form.find('input[required], select[required], textarea[required]').each(function () {
				if (!window.formValidation.validateField(this)) {
					isValid = false;
				}
			});

			if (!isValid) {
				event.preventDefault();
			}
		});
	}
};

document.addEventListener('DOMContentLoaded', function () {
	const toggle = document.getElementById('sidebarToggle');
	const body = document.body;
	const LS_KEY = 'cms.sidebar.collapsed';

	window.showToast = function showToast(message, title, variant) {
		const toastEl = document.getElementById('liveToast');
		const titleEl = document.getElementById('toastTitle');
		const bodyEl = document.getElementById('toastBody');

		if (!toastEl || !titleEl || !bodyEl) {
			return;
		}

		titleEl.textContent = title || 'Notification';
		bodyEl.textContent = message || '';

		toastEl.className = 'toast';
		if (variant) {
			toastEl.classList.add(`text-bg-${variant}`);
		}

		const toast = bootstrap.Toast.getOrCreateInstance(toastEl);
		toast.show();
	};

	// initialize state
	const collapsed = localStorage.getItem(LS_KEY) === '1';
	if (collapsed) {
		body.classList.add('sidebar-collapsed');
		document.documentElement.classList.add('sidebar-collapsed');
	}

	// toggle for desktop collapse
	if (toggle) {
		toggle.addEventListener('click', function (e) {
			e.preventDefault();
			const isCollapsed = body.classList.toggle('sidebar-collapsed');
			// keep html element in sync so CSS applied early and stays consistent
			if (isCollapsed) document.documentElement.classList.add('sidebar-collapsed');
			else document.documentElement.classList.remove('sidebar-collapsed');
			localStorage.setItem(LS_KEY, isCollapsed ? '1' : '0');
		});
	}

	// on small screens, toggle overlay when brand clicked
	const brand = document.getElementById('brandLink');
	if (brand) {
		brand.addEventListener('click', function (e) {
			if (window.innerWidth < 768) {
				e.preventDefault();
				body.classList.toggle('sidebar-open');
			}
		});
	}

	// close overlay on click outside
	document.addEventListener('click', function (e) {
		if (window.innerWidth < 768 && body.classList.contains('sidebar-open')) {
			const sidebar = document.querySelector('.sidebar');
			if (sidebar && !sidebar.contains(e.target) && !e.target.closest('#sidebarToggle')) {
				body.classList.remove('sidebar-open');
			}
		}
	});

	// task board drag/drop and filters
	const taskBoard = document.querySelector('.task-board');
	if (taskBoard) {
		const searchInput = document.getElementById('taskSearchInput');
		const statusFilter = document.getElementById('taskStatusFilter');
		const assigneeFilter = document.getElementById('taskAssigneeFilter');
		const resetFilters = document.getElementById('taskResetFilters');
		const cards = Array.from(taskBoard.querySelectorAll('.task-card[draggable="true"]'));
		const columns = Array.from(taskBoard.querySelectorAll('.task-column'));

		function updateCardVisibility() {
			const searchValue = searchInput?.value.trim().toLowerCase() || '';
			const statusValue = statusFilter?.value || '';
			const assigneeValue = assigneeFilter?.value || '';

			cards.forEach(card => {
				const title = (card.dataset.title || '').toLowerCase();
				const assigned = (card.dataset.assigned || '').toLowerCase();
				const status = card.dataset.status || '';
				const matchesSearch = searchValue === '' || title.includes(searchValue) || assigned.includes(searchValue);
				const matchesStatus = statusValue === '' || status === statusValue;
				const matchesAssignee = assigneeValue === '' || (assigned && assigned === assigneeValue.toLowerCase());
				card.style.display = matchesSearch && matchesStatus && matchesAssignee ? '' : 'none';
			});
		}

		function handleDragStart(e) {
			e.currentTarget.classList.add('dragging');
			e.dataTransfer.effectAllowed = 'move';
			e.dataTransfer.setData('text/plain', e.currentTarget.dataset.taskid || '');
		}

		function handleDragEnd(e) {
			e.currentTarget.classList.remove('dragging');
			columns.forEach(col => col.classList.remove('drag-over'));
		}

		function handleDragOver(e) {
			e.preventDefault();
			if (e.currentTarget.classList.contains('task-column')) {
				e.currentTarget.classList.add('drag-over');
			}
		}

		function handleDragLeave(e) {
			e.currentTarget.classList.remove('drag-over');
		}

		function handleDrop(e) {
			e.preventDefault();
			const taskId = e.dataTransfer.getData('text/plain');
			const draggedCard = taskBoard.querySelector(`.task-card[data-taskid="${taskId}"]`);
			if (draggedCard && e.currentTarget.classList.contains('task-column')) {
				const dropContainer = e.currentTarget.querySelector('.d-flex.flex-column') || e.currentTarget;
				if (dropContainer) {
					dropContainer.appendChild(draggedCard);
					const newStatus = e.currentTarget.dataset.status;
					if (newStatus) {
						draggedCard.dataset.status = newStatus;
					}
				}
			}
			e.currentTarget.classList.remove('drag-over');
		}

		cards.forEach(card => {
			card.addEventListener('dragstart', handleDragStart);
			card.addEventListener('dragend', handleDragEnd);
		});

		columns.forEach(column => {
			column.addEventListener('dragover', handleDragOver);
			column.addEventListener('dragleave', handleDragLeave);
			column.addEventListener('drop', handleDrop);
		});

		if (searchInput) {
			searchInput.addEventListener('input', updateCardVisibility);
		}
		if (statusFilter) {
			statusFilter.addEventListener('change', updateCardVisibility);
		}
		if (assigneeFilter) {
			assigneeFilter.addEventListener('change', updateCardVisibility);
		}
		if (resetFilters) {
			resetFilters.addEventListener('click', function () {
				if (searchInput) searchInput.value = '';
				if (statusFilter) statusFilter.value = '';
				if (assigneeFilter) assigneeFilter.value = '';
				updateCardVisibility();
			});
		}
	}
});
